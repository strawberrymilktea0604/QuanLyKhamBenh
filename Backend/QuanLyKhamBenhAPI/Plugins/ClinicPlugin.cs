using System.ComponentModel;
using Microsoft.SemanticKernel;
using QuanLyKhamBenhAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace QuanLyKhamBenhAPI.Plugins;

/// <summary>
/// Plugin cho Semantic Kernel để AI có thể truy cập toàn bộ cơ sở dữ liệu của hệ thống trong phạm vi user
/// 
/// Các chức năng có sẵn:
/// - Lịch sử khám bệnh và hồ sơ bệnh án
/// - Lịch hẹn hiện tại và tương lai
/// - Thông tin bác sĩ và chuyên khoa
/// - Ca làm việc của bác sĩ
/// - Dịch vụ khám bệnh và giá cả
/// - Điểm tích lũy và khuyến mãi
/// - Lịch sử thanh toán
/// - Đánh giá và phản hồi
/// - Tìm kiếm và tra cứu thông tin
/// </summary>
public class ClinicPlugin
{
    private readonly QuanLyKhamBenhContext _context;
    private readonly ILogger<ClinicPlugin> _logger;

    public ClinicPlugin(QuanLyKhamBenhContext context, ILogger<ClinicPlugin> logger)
    {
        _context = context;
        _logger = logger;
    }

    [KernelFunction, Description("Lấy lịch sử khám bệnh 5 lần gần nhất của bệnh nhân (chỉ những lần ĐÃ KHÁM)")]
    public async Task<string> LayLichSuKhamAsync(
        [Description("Mã bệnh nhân (Patient ID)")] int patientId)
    {
        try
        {
            _logger.LogInformation("LayLichSuKhamAsync called for patient {PatientId}", patientId);
            
            // LOGIC HỢP LÝ: Lịch sử khám = những lần ĐÃ HOÀN THÀNH (Completed)
            // Chỉ có MedicalRecord mới là đã khám xong và có kết quả
            var records = await _context.MedicalRecords
                .Include(mr => mr.Appointment)
                    .ThenInclude(a => a!.Doctor)
                        .ThenInclude(d => d!.Specialty)
                .Include(mr => mr.Appointment)
                    .ThenInclude(a => a!.Patient)
                .Where(mr => mr.Appointment!.PatientId == patientId 
                    && mr.Appointment.Status == "Completed")
                .OrderByDescending(mr => mr.CreatedDate)
                .Take(5)
                .Select(mr => new
                {
                    Ngay = mr.Appointment!.Date.ToString("dd/MM/yyyy"),
                    BacSi = mr.Appointment.Doctor!.Name,
                    ChuyenKhoa = mr.Appointment.Doctor.Specialty!.Name,
                    TrieuChung = mr.Symptoms,
                    ChanDoan = mr.Diagnosis,
                    DieuTri = mr.Treatment
                })
                .ToListAsync();

            _logger.LogInformation("Found {Count} completed medical records", records.Count);

            if (!records.Any())
            {
                return "Bệnh nhân chưa có lịch sử khám bệnh.";
            }

            return JsonSerializer.Serialize(records, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting patient history");
            return "Không thể lấy lịch sử khám bệnh.";
        }
    }

    [KernelFunction, Description("Lấy danh sách lịch hẹn sắp tới của bệnh nhân")]
    public async Task<string> LayLichHenSapToiAsync(
        [Description("Mã bệnh nhân")] int patientId)
    {
        try
        {
            _logger.LogInformation("LayLichHenSapToiAsync called with patientId: {PatientId}", patientId);
            
            var today = DateOnly.FromDateTime(DateTime.Today);
            _logger.LogInformation("Today date: {Today}", today);
            
            // Lấy TẤT CẢ appointments của bệnh nhân để debug
            var allAppointments = await _context.Appointments
                .Where(a => a.PatientId == patientId)
                .Select(a => new { a.AppointmentId, a.Date, a.Status })
                .ToListAsync();
            
            _logger.LogInformation("========================================");
            _logger.LogInformation("DEBUG: Total appointments for patient {PatientId}: {Count}", patientId, allAppointments.Count);
            foreach (var apt in allAppointments)
            {
                _logger.LogInformation("  → Appointment #{Id}: Date={Date}, Status='{Status}'", apt.AppointmentId, apt.Date, apt.Status ?? "NULL");
            }
            _logger.LogInformation("DEBUG: Today = {Today}", today);
            _logger.LogInformation("DEBUG: Valid statuses = Pending, Confirmed");
            _logger.LogInformation("========================================");
            
            // LOGIC HỢP LÝ: Chỉ lấy lịch hẹn CHƯA HOÀN THÀNH
            // - Bỏ "Completed": Đã khám xong
            // - Bỏ "Cancelled": Đã hủy
            // - Lấy "Scheduled": Đã lên lịch (status mặc định khi tạo mới)
            // - Lấy "Pending": Chờ xác nhận
            // - Lấy "Confirmed": Đã xác nhận
            var validStatuses = new[] { "Scheduled", "Pending", "Confirmed" };
            
            var appointments = await _context.Appointments
                .Include(a => a.Doctor)
                    .ThenInclude(d => d!.Specialty)
                .Where(a => a.PatientId == patientId 
                    && a.Date >= today
                    && validStatuses.Contains(a.Status))
                .OrderBy(a => a.Date)
                .ThenBy(a => a.Time)
                .Take(5)
                .Select(a => new
                {
                    Ngay = a.Date.ToString("dd/MM/yyyy"),
                    Gio = a.Time.ToString(@"hh\:mm"),
                    BacSi = a.Doctor!.Name,
                    ChuyenKhoa = a.Doctor.Specialty!.Name,
                    TrangThai = a.Status
                })
                .ToListAsync();

            _logger.LogInformation("DEBUG: Found {Count} VALID upcoming appointments", appointments.Count);
            
            if (appointments.Count == 0)
            {
                _logger.LogWarning("NO VALID APPOINTMENTS FOUND! Possible reasons:");
                _logger.LogWarning("  1. All appointments have Status != 'Pending' or 'Confirmed'");
                _logger.LogWarning("  2. All appointments are in the past (< {Today})", today);
                _logger.LogWarning("  3. No appointments exist for patient {PatientId}", patientId);
            }

            if (!appointments.Any())
            {
                return "Bệnh nhân không có lịch hẹn sắp tới.";
            }

            var result = JsonSerializer.Serialize(appointments, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            
            _logger.LogInformation("Returning result: {Result}", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming appointments for patient {PatientId}", patientId);
            return "Không thể lấy lịch hẹn sắp tới.";
        }
    }

    [KernelFunction, Description("Lấy danh sách bác sĩ theo chuyên khoa")]
    public async Task<string> LayDanhSachBacSiAsync(
        [Description("Tên chuyên khoa (ví dụ: Nội khoa, Ngoại khoa)")] string? chuyenKhoa = null)
    {
        try
        {
            var query = _context.Doctors
                .Include(d => d.Specialty)
                .AsQueryable();

            if (!string.IsNullOrEmpty(chuyenKhoa))
            {
                query = query.Where(d => d.Specialty!.Name!.Contains(chuyenKhoa));
            }

            var doctors = await query
                .Select(d => new
                {
                    MaBacSi = d.DoctorId,
                    TenBacSi = d.Name,
                    ChuyenKhoa = d.Specialty!.Name,
                    SoDienThoai = d.Phone
                })
                .ToListAsync();

            if (!doctors.Any())
            {
                return chuyenKhoa != null 
                    ? $"Không tìm thấy bác sĩ chuyên khoa {chuyenKhoa}."
                    : "Không tìm thấy bác sĩ.";
            }

            return JsonSerializer.Serialize(doctors, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting doctors list");
            return "Không thể lấy danh sách bác sĩ.";
        }
    }

    [KernelFunction, Description("Lấy danh sách ca làm việc trống của bác sĩ trong tuần")]
    public async Task<string> LayCaLamViecTrongAsync(
        [Description("Mã bác sĩ")] int doctorId,
        [Description("Ngày bắt đầu (format: yyyy-MM-dd)")] string? startDate = null)
    {
        try
        {
            var start = string.IsNullOrEmpty(startDate) 
                ? DateOnly.FromDateTime(DateTime.Today)
                : DateOnly.Parse(startDate);
            
            var end = start.AddDays(7);

            var workShifts = await _context.WorkShifts
                .Where(ws => ws.DoctorId == doctorId 
                    && ws.Date >= start 
                    && ws.Date <= end)
                .OrderBy(ws => ws.Date)
                .ThenBy(ws => ws.StartTime)
                .Select(ws => new
                {
                    Ngay = ws.Date.ToString("dd/MM/yyyy"),
                    GioBatDau = ws.StartTime.ToString(@"hh\:mm"),
                    GioKetThuc = ws.EndTime.ToString(@"hh\:mm")
                })
                .ToListAsync();

            if (!workShifts.Any())
            {
                return "Bác sĩ không có ca làm việc trong tuần này.";
            }

            return JsonSerializer.Serialize(workShifts, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting work shifts");
            return "Không thể lấy ca làm việc.";
        }
    }

    [KernelFunction, Description("Tìm kiếm thông tin về dịch vụ khám bệnh và giá")]
    public async Task<string> TimDichVuKhamAsync(
        [Description("Từ khóa tìm kiếm dịch vụ")] string keyword)
    {
        try
        {
            _logger.LogInformation("TimDichVuKhamAsync called with keyword: {Keyword}", keyword);
            
            // Nếu không có keyword hoặc keyword chung chung, trả về TẤT CẢ dịch vụ
            var query = _context.Services.AsQueryable();
            
            if (!string.IsNullOrWhiteSpace(keyword) && 
                keyword != "dịch vụ" && 
                keyword != "y tế" && 
                keyword != "khám")
            {
                query = query.Where(s => s.Name!.Contains(keyword) || s.Type!.Contains(keyword));
            }
            
            var services = await query
                .Select(s => new
                {
                    TenDichVu = s.Name,
                    LoaiDichVu = s.Type,
                    Gia = s.Price
                })
                .Take(10)
                .ToListAsync();

            _logger.LogInformation("Found {Count} services", services.Count);

            if (!services.Any())
            {
                return $"Không tìm thấy dịch vụ với từ khóa '{keyword}'.";
            }

            var result = JsonSerializer.Serialize(services, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            
            _logger.LogInformation("Returning {Count} services", services.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching services with keyword: {Keyword}", keyword);
            return "Không thể tìm kiếm dịch vụ.";
        }
    }

    [KernelFunction, Description("Tra cứu kết quả xét nghiệm của bệnh nhân")]
    public async Task<string> TraKetQuaXetNghiemAsync(
        [Description("Mã bệnh nhân")] int patientId)
    {
        try
        {
            _logger.LogInformation("TraKetQuaXetNghiemAsync called for patient {PatientId}", patientId);
            
            // Lấy 5 kết quả xét nghiệm gần nhất
            var labResults = await _context.LabResults
                .Include(lr => lr.Record)
                    .ThenInclude(r => r!.Appointment)
                        .ThenInclude(a => a!.Doctor)
                            .ThenInclude(d => d!.Specialty)
                .Where(lr => lr.Record!.Appointment!.PatientId == patientId 
                    && lr.ResultDate.HasValue)
                .OrderByDescending(lr => lr.ResultDate)
                .Take(5)
                .Select(lr => new
                {
                    NgayXetNghiem = lr.ResultDate!.Value.ToString("dd/MM/yyyy"),
                    KetQua = lr.ResultDetails ?? "Chưa có kết quả chi tiết",
                    BacSiYeuCau = lr.Record!.Appointment!.Doctor!.Name,
                    ChuyenKhoa = lr.Record!.Appointment!.Doctor!.Specialty!.Name
                })
                .ToListAsync();

            _logger.LogInformation("Found {Count} lab results", labResults.Count);

            if (!labResults.Any())
            {
                return "Bệnh nhân chưa có kết quả xét nghiệm nào.";
            }

            var result = JsonSerializer.Serialize(labResults, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting lab results for patient {PatientId}", patientId);
            return "Không thể tra cứu kết quả xét nghiệm.";
        }
    }

    [KernelFunction, Description("Lấy thông tin điểm tích lũy của bệnh nhân")]
    public async Task<string> LayDiemTichLuyAsync(
        [Description("Mã bệnh nhân")] int patientId)
    {
        try
        {
            var loyaltyPoint = await _context.LoyaltyPoints
                .FirstOrDefaultAsync(lp => lp.PatientId == patientId);

            if (loyaltyPoint == null)
            {
                return "Bệnh nhân chưa có điểm tích lũy.";
            }

            return JsonSerializer.Serialize(new
            {
                DiemHienTai = loyaltyPoint.Points ?? 0,
                NgayCapNhatCuoi = loyaltyPoint.LastUpdated?.ToString("dd/MM/yyyy HH:mm")
            }, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting loyalty points");
            return "Không thể lấy điểm tích lũy.";
        }
    }

    [KernelFunction, Description("Lấy danh sách khuyến mãi đang hoạt động")]
    public async Task<string> LayDanhSachKhuyenMaiAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var promotions = await _context.Promotions
                .Where(p => p.StartDate <= today && p.EndDate >= today)
                .ToListAsync();

            var promotionData = promotions.Select(p => new
            {
                TenKhuyenMai = p.Description,
                PhanTramGiam = p.DiscountPercent,
                NgayBatDau = p.StartDate?.ToString("dd/MM/yyyy"),
                NgayKetThuc = p.EndDate?.ToString("dd/MM/yyyy")
            }).ToList();

            if (!promotions.Any())
            {
                return "Hiện không có khuyến mãi nào đang hoạt động.";
            }

            return JsonSerializer.Serialize(promotionData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting promotions");
            return "Không thể lấy danh sách khuyến mãi.";
        }
    }

    [KernelFunction, Description("Lấy lịch sử thanh toán của bệnh nhân")]
    public async Task<string> LayLichSuThanhToanAsync(
        [Description("Mã bệnh nhân")] int patientId)
    {
        try
        {
            var payments = await _context.Payments
                .Include(p => p.Appointment)
                    .ThenInclude(a => a!.Doctor)
                        .ThenInclude(d => d!.Specialty)
                .Where(p => p.Appointment!.PatientId == patientId)
                .OrderByDescending(p => p.PaymentDate)
                .Take(5)
                .ToListAsync();

            var paymentData = payments.Select(p => new
            {
                MaThanhToan = p.PaymentId,
                NgayThanhToan = p.PaymentDate?.ToString("dd/MM/yyyy HH:mm"),
                SoTien = p.TotalAmount,
                PhuongThuc = p.PaymentMethod,
                TrangThai = p.Status,
                BacSi = p.Appointment!.Doctor!.Name,
                ChuyenKhoa = p.Appointment.Doctor.Specialty!.Name
            }).ToList();

            if (!payments.Any())
            {
                return "Bệnh nhân chưa có lịch sử thanh toán.";
            }

            return JsonSerializer.Serialize(paymentData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment history");
            return "Không thể lấy lịch sử thanh toán.";
        }
    }

    [KernelFunction, Description("Lấy danh sách đánh giá của bệnh nhân")]
    public async Task<string> LayDanhGiaCuaBenhNhanAsync(
        [Description("Mã bệnh nhân")] int patientId)
    {
        try
        {
            var feedbacks = await _context.Feedbacks
                .Include(f => f.Doctor)
                    .ThenInclude(d => d!.Specialty)
                .Where(f => f.PatientId == patientId)
                .OrderByDescending(f => f.CreatedDate)
                .ToListAsync();

            var feedbackData = feedbacks.Select(f => new
            {
                MaDanhGia = f.FeedbackId,
                BacSi = f.Doctor!.Name,
                ChuyenKhoa = f.Doctor.Specialty!.Name,
                Sao = f.Rating,
                NhanXet = f.Comment,
                NgayDanhGia = f.CreatedDate?.ToString("dd/MM/yyyy")
            }).ToList();

            if (!feedbacks.Any())
            {
                return "Bệnh nhân chưa có đánh giá nào.";
            }

            return JsonSerializer.Serialize(feedbackData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting patient feedback");
            return "Không thể lấy danh sách đánh giá.";
        }
    }

    [KernelFunction, Description("Lấy danh sách tất cả chuyên khoa")]
    public async Task<string> LayDanhSachChuyenKhoaAsync()
    {
        try
        {
            var specialties = await _context.Specialties
                .Select(s => new
                {
                    MaChuyenKhoa = s.SpecialtyId,
                    TenChuyenKhoa = s.Name,
                    MoTa = s.Description
                })
                .ToListAsync();

            if (!specialties.Any())
            {
                return "Không có chuyên khoa nào.";
            }

            return JsonSerializer.Serialize(specialties, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting specialties");
            return "Không thể lấy danh sách chuyên khoa.";
        }
    }

    [KernelFunction, Description("Lấy thông tin chi tiết của bác sĩ")]
    public async Task<string> LayThongTinBacSiAsync(
        [Description("Mã bác sĩ")] int doctorId)
    {
        try
        {
            var doctor = await _context.Doctors
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId);

            if (doctor == null)
            {
                return "Không tìm thấy bác sĩ.";
            }

            // Lấy số lượng đánh giá và điểm trung bình
            var feedbackStats = await _context.Feedbacks
                .Where(f => f.DoctorId == doctorId)
                .GroupBy(f => 1)
                .Select(g => new
                {
                    SoLuongDanhGia = g.Count(),
                    DiemTrungBinh = g.Average(f => f.Rating)
                })
                .FirstOrDefaultAsync();

            var result = new
            {
                MaBacSi = doctor.DoctorId,
                TenBacSi = doctor.Name,
                ChuyenKhoa = doctor.Specialty?.Name,
                SoDienThoai = doctor.Phone,
                SoLuongDanhGia = feedbackStats?.SoLuongDanhGia ?? 0,
                DiemTrungBinh = feedbackStats?.DiemTrungBinh ?? 0
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting doctor info");
            return "Không thể lấy thông tin bác sĩ.";
        }
    }

    [KernelFunction, Description("Lấy lịch sử đặt lịch của bệnh nhân")]
    public async Task<string> LayLichSuDatLichAsync(
        [Description("Mã bệnh nhân")] int patientId)
    {
        try
        {
            var appointments = await _context.Appointments
                .Include(a => a.Doctor)
                    .ThenInclude(d => d!.Specialty)
                .Where(a => a.PatientId == patientId)
                .OrderByDescending(a => a.Date)
                .ThenByDescending(a => a.Time)
                .Take(10)
                .Select(a => new
                {
                    MaLichHen = a.AppointmentId,
                    Ngay = a.Date.ToString("dd/MM/yyyy"),
                    Gio = a.Time.ToString(@"hh\:mm"),
                    BacSi = a.Doctor!.Name,
                    ChuyenKhoa = a.Doctor.Specialty!.Name,
                    TrangThai = a.Status
                })
                .ToListAsync();

            if (!appointments.Any())
            {
                return "Bệnh nhân chưa có lịch sử đặt lịch.";
            }

            return JsonSerializer.Serialize(appointments, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting appointment history");
            return "Không thể lấy lịch sử đặt lịch.";
        }
    }

    [KernelFunction, Description("Tìm kiếm bác sĩ theo tên")]
    public async Task<string> TimBacSiTheoTenAsync(
        [Description("Tên bác sĩ cần tìm")] string tenBacSi)
    {
        try
        {
            var doctors = await _context.Doctors
                .Include(d => d.Specialty)
                .Where(d => d.Name!.Contains(tenBacSi))
                .Select(d => new
                {
                    MaBacSi = d.DoctorId,
                    TenBacSi = d.Name,
                    ChuyenKhoa = d.Specialty!.Name,
                    SoDienThoai = d.Phone
                })
                .ToListAsync();

            if (!doctors.Any())
            {
                return $"Không tìm thấy bác sĩ có tên '{tenBacSi}'.";
            }

            return JsonSerializer.Serialize(doctors, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching doctors by name");
            return "Không thể tìm kiếm bác sĩ.";
        }
    }
}
