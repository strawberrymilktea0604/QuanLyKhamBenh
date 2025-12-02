namespace QuanLyKhamBenhAPI.Services
{
    public interface IBackupRestoreService
    {
        Task<string> BackupDatabaseAsync(string fileName);
        Task<bool> RestoreDatabaseAsync(string fileName);
        Task<List<BackupFileInfo>> GetBackupFilesAsync();
        Task<bool> DeleteBackupFileAsync(string fileName);
    }

    public class BackupFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeDisplay { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
    }
}
