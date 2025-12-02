using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhamBenhAPI.Services;

namespace QuanLyKhamBenhAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class BackupRestoreController : ControllerBase
    {
        private readonly IBackupRestoreService _backupRestoreService;
        private readonly ILogger<BackupRestoreController> _logger;

        public BackupRestoreController(
            IBackupRestoreService backupRestoreService,
            ILogger<BackupRestoreController> logger)
        {
            _backupRestoreService = backupRestoreService;
            _logger = logger;
        }

        /// <summary>
        /// Lấy danh sách tất cả file backup
        /// </summary>
        [HttpGet("files")]
        public async Task<IActionResult> GetBackupFiles()
        {
            try
            {
                var files = await _backupRestoreService.GetBackupFilesAsync();
                return Ok(new
                {
                    success = true,
                    data = files,
                    count = files.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách file backup");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Không thể lấy danh sách file backup",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Tạo file backup mới
        /// </summary>
        [HttpPost("backup")]
        public async Task<IActionResult> BackupDatabase([FromBody] BackupRequest request)
        {
            try
            {
                var fileName = await _backupRestoreService.BackupDatabaseAsync(request.FileName);
                return Ok(new
                {
                    success = true,
                    message = "Backup database thành công",
                    fileName = fileName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi backup database");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Không thể backup database",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Restore database từ file backup
        /// </summary>
        [HttpPost("restore")]
        public async Task<IActionResult> RestoreDatabase([FromBody] RestoreRequest request)
        {
            try
            {
                var result = await _backupRestoreService.RestoreDatabaseAsync(request.FileName);
                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Restore database thành công"
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Restore database thất bại"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi restore database");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Không thể restore database",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Xóa file backup
        /// </summary>
        [HttpDelete("files/{fileName}")]
        public async Task<IActionResult> DeleteBackupFile(string fileName)
        {
            try
            {
                var result = await _backupRestoreService.DeleteBackupFileAsync(fileName);
                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Xóa file backup thành công"
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Xóa file backup thất bại"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa file backup");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Không thể xóa file backup",
                    error = ex.Message
                });
            }
        }
    }

    public class BackupRequest
    {
        public string FileName { get; set; } = string.Empty;
    }

    public class RestoreRequest
    {
        public string FileName { get; set; } = string.Empty;
    }
}
