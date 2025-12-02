using Microsoft.Data.SqlClient;
using System.Data;

namespace QuanLyKhamBenhAPI.Services
{
    public class BackupRestoreService : IBackupRestoreService
    {
        private readonly string _defaultConnectionString;
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly string _backupFolderPath;
        private readonly ILogger<BackupRestoreService> _logger;

        public BackupRestoreService(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<BackupRestoreService> logger)
        {
            _defaultConnectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection not found");
            
            _masterConnectionString = configuration.GetConnectionString("MasterConnection") 
                ?? throw new InvalidOperationException("MasterConnection not found");

            _logger = logger;

            // Lấy tên database từ connection string
            var builder = new SqlConnectionStringBuilder(_defaultConnectionString);
            _databaseName = builder.InitialCatalog;

            // Sử dụng thư mục backup mặc định của SQL Server
            // Thư mục này SQL Server luôn có quyền truy cập
            _backupFolderPath = @"C:\Temp\SQLBackups";
            if (!Directory.Exists(_backupFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(_backupFolderPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Không thể tạo thư mục {_backupFolderPath}, sử dụng thư mục temp");
                    _backupFolderPath = Path.GetTempPath();
                }
            }
        }

        public async Task<string> BackupDatabaseAsync(string fileName)
        {
            try
            {
                // Tạo tên file với timestamp nếu không có
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = $"{_databaseName}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                }
                else if (!fileName.EndsWith(".bak"))
                {
                    fileName += ".bak";
                }

                var backupFilePath = Path.Combine(_backupFolderPath, fileName);

                // Kiểm tra file đã tồn tại
                if (File.Exists(backupFilePath))
                {
                    throw new InvalidOperationException($"File backup '{fileName}' đã tồn tại");
                }

                // Thực hiện backup
                using (var connection = new SqlConnection(_defaultConnectionString))
                {
                    await connection.OpenAsync();

                    var sql = $@"
                        BACKUP DATABASE [{_databaseName}]
                        TO DISK = @backupPath
                        WITH FORMAT,
                        MEDIANAME = 'QuanLyKhamBenhBackups',
                        NAME = 'Full Backup of {_databaseName}';
                    ";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 600; // 10 phút
                        command.Parameters.AddWithValue("@backupPath", backupFilePath);
                        await command.ExecuteNonQueryAsync();
                    }
                }

                _logger.LogInformation($"Backup thành công: {fileName}");
                return fileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi backup database: {ex.Message}");
                throw new InvalidOperationException($"Không thể backup database: {ex.Message}");
            }
        }

        public async Task<bool> RestoreDatabaseAsync(string fileName)
        {
            try
            {
                var backupFilePath = Path.Combine(_backupFolderPath, fileName);

                // Kiểm tra file tồn tại
                if (!File.Exists(backupFilePath))
                {
                    throw new FileNotFoundException($"Không tìm thấy file backup: {fileName}");
                }

                // Kết nối tới master database để restore
                using (var connection = new SqlConnection(_masterConnectionString))
                {
                    await connection.OpenAsync();

                    // Script để đá người dùng + restore + mở lại
                    var sql = $@"
                        -- Chuyển database sang chế độ SINGLE_USER và đá tất cả kết nối
                        ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

                        -- Thực hiện restore
                        RESTORE DATABASE [{_databaseName}]
                        FROM DISK = @backupPath
                        WITH REPLACE;

                        -- Chuyển lại sang MULTI_USER
                        ALTER DATABASE [{_databaseName}] SET MULTI_USER;
                    ";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 600; // 10 phút
                        command.Parameters.AddWithValue("@backupPath", backupFilePath);
                        await command.ExecuteNonQueryAsync();
                    }
                }

                _logger.LogInformation($"Restore thành công từ file: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi restore database: {ex.Message}");
                
                // Cố gắng đưa database về MULTI_USER nếu có lỗi
                try
                {
                    using (var connection = new SqlConnection(_masterConnectionString))
                    {
                        await connection.OpenAsync();
                        var recoverySql = $"ALTER DATABASE [{_databaseName}] SET MULTI_USER;";
                        using (var command = new SqlCommand(recoverySql, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch { }

                throw new InvalidOperationException($"Không thể restore database: {ex.Message}");
            }
        }

        public async Task<List<BackupFileInfo>> GetBackupFilesAsync()
        {
            return await Task.Run(() =>
            {
                var backupFiles = new List<BackupFileInfo>();

                if (!Directory.Exists(_backupFolderPath))
                {
                    return backupFiles;
                }

                var files = Directory.GetFiles(_backupFolderPath, "*.bak")
                    .OrderByDescending(f => new FileInfo(f).CreationTime);

                foreach (var filePath in files)
                {
                    var fileInfo = new FileInfo(filePath);
                    backupFiles.Add(new BackupFileInfo
                    {
                        FileName = fileInfo.Name,
                        FilePath = filePath,
                        FileSizeBytes = fileInfo.Length,
                        FileSizeDisplay = FormatFileSize(fileInfo.Length),
                        CreatedDate = fileInfo.CreationTime
                    });
                }

                return backupFiles;
            });
        }

        public async Task<bool> DeleteBackupFileAsync(string fileName)
        {
            try
            {
                var backupFilePath = Path.Combine(_backupFolderPath, fileName);

                if (!File.Exists(backupFilePath))
                {
                    throw new FileNotFoundException($"Không tìm thấy file backup: {fileName}");
                }

                await Task.Run(() => File.Delete(backupFilePath));
                _logger.LogInformation($"Đã xóa file backup: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi xóa file backup: {ex.Message}");
                throw new InvalidOperationException($"Không thể xóa file backup: {ex.Message}");
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
