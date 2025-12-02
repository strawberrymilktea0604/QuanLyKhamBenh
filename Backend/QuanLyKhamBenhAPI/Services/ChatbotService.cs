/*
 * ChatbotService - Enhanced with Rate Limiting and Caching
 * 
 * Current Model: Gemini 2.0 Flash Lite
 * 
 * Features implemented to prevent Gemini API quota exhaustion:
 * 
 * 1. RATE LIMITING:
 *    - Sliding window rate limiter: 10 requests per minute
 *    - Allows queuing up to 5 requests when limit is reached
 *    - Prevents overwhelming the Gemini API
 * 
 * 2. RESPONSE CACHING:
 *    - Caches responses for 30 minutes based on message hash and context
 *    - Reduces API calls for similar questions
 *    - Automatic cleanup of expired cache entries
 * 
 * 3. RETRY LOGIC:
 *    - Exponential backoff retry (1s, 2s, 4s) for 429 errors
 *    - Up to 3 retry attempts before giving up
 *    - Graceful degradation with user-friendly messages
 * 
 * 4. SEQUENTIAL PROCESSING:
 *    - Semaphore ensures only one request processed at a time
 *    - Prevents parallel requests that could hit rate limits
 *    - Maintains conversation context integrity
 * 
 * Usage: The service automatically handles rate limiting and caching.
 * No changes needed in controller code.
 */

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using QuanLyKhamBenhAPI.Models;
using QuanLyKhamBenhAPI.Plugins;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using System.Threading;

namespace QuanLyKhamBenhAPI.Services;

public class ChatbotService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<ChatbotService> _logger;
    
    // Rate limiting
    private static readonly ConcurrentDictionary<string, RateLimiter> _rateLimiters = new();
    private static readonly ConcurrentDictionary<string, string> _responseCache = new();
    private static readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(30);
    private static readonly ConcurrentDictionary<string, DateTime> _cacheTimestamps = new();
    
    // Queue for sequential processing
    private static readonly SemaphoreSlim _processingSemaphore = new(1, 1);

    public ChatbotService(
        IConfiguration configuration, 
        ILogger<ChatbotService> logger,
        QuanLyKhamBenhContext context)
    {
        _logger = logger;
        
        var geminiApiKey = configuration["Gemini:ApiKey"] 
            ?? throw new ArgumentException("Gemini API Key is not configured");

        _logger.LogInformation("Initializing Gemini with API Key: {Key}", 
            geminiApiKey.Substring(0, Math.Min(10, geminiApiKey.Length)) + "...");

        // Khởi tạo Semantic Kernel với Gemini và Plugin
        var builder = Kernel.CreateBuilder();
        
#pragma warning disable SKEXP0070
        try
        {
            builder.AddGoogleAIGeminiChatCompletion(
                modelId: "gemini-2.0-flash-lite",
                apiKey: geminiApiKey);
            _logger.LogInformation("Successfully configured Gemini model: gemini-2.0-flash-lite");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure Gemini connector");
            throw;
        }
#pragma warning restore SKEXP0070
        
        // Thêm plugin để AI có thể gọi các hàm nghiệp vụ
        var pluginLogger = new LoggerFactory().CreateLogger<ClinicPlugin>();
        builder.Plugins.AddFromObject(new ClinicPlugin(context, pluginLogger));

        _kernel = builder.Build();
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
    }

    public async Task<string> GetChatResponseAsync(
        string userMessage, 
        int? patientId = null,
        PatientContextData? contextData = null,
        ChatHistory? history = null)
    {
        // Rate limiting check
        if (!await CheckRateLimitAsync("global"))
        {
            _logger.LogWarning("Rate limit exceeded for chatbot requests");
            return "Xin lỗi, dịch vụ tư vấn đang bận. Vui lòng thử lại sau vài phút.";
        }

        // TẮT CACHE để luôn lấy dữ liệu mới từ database
        // Cache gây ra vấn đề: dữ liệu cũ được lưu 30 phút, không realtime
        // var cacheKey = GenerateCacheKey(userMessage, patientId);
        // if (_responseCache.TryGetValue(cacheKey, out var cachedResponse) && 
        //     _cacheTimestamps.TryGetValue(cacheKey, out var cacheTime) &&
        //     DateTime.UtcNow - cacheTime < _cacheExpiration)
        // {
        //     _logger.LogInformation("Returning cached response for message: {Message}", userMessage);
        //     return cachedResponse;
        // }

        // Use semaphore to ensure sequential processing
        await _processingSemaphore.WaitAsync();
        try
        {
            return await GetChatResponseInternalAsync(userMessage, patientId, contextData, history, null);
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    private async Task<string> GetChatResponseInternalAsync(
        string userMessage, 
        int? patientId,
        PatientContextData? contextData,
        ChatHistory? history,
        string cacheKey)
    {
        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromSeconds(1);

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Tạo chat history mới nếu chưa có
                history ??= new ChatHistory();

                // Thêm system prompt với context data
                if (history.Count == 0)
                {
                    var systemPrompt = BuildSystemPrompt(patientId, contextData);
                    history.AddSystemMessage(systemPrompt);
                }

                // Thêm tin nhắn người dùng
                history.AddUserMessage(userMessage);

                // Gọi Gemini với auto function calling enabled
#pragma warning disable SKEXP0070
                var settings = new GeminiPromptExecutionSettings
                {
                    ToolCallBehavior = GeminiToolCallBehavior.AutoInvokeKernelFunctions,
                    Temperature = 0.4,  // Giảm xuống để câu trả lời ngắn gọn, ít "suy nghĩ" hơn
                    TopP = 0.9,         // Giảm đa dạng để tập trung vào thông tin chính
                    MaxTokens = 300     // Giới hạn nghiêm ngặt hơn để tránh dài dòng
                };
#pragma warning restore SKEXP0070

                var response = await _chatService.GetChatMessageContentAsync(
                    history, 
                    executionSettings: settings,
                    kernel: _kernel);

                var result = response.Content ?? "Xin lỗi, tôi không thể trả lời câu hỏi này.";
                
                // TẮT CACHE để đảm bảo dữ liệu realtime
                // if (!string.IsNullOrEmpty(cacheKey))
                // {
                //     _responseCache[cacheKey] = result;
                //     _cacheTimestamps[cacheKey] = DateTime.UtcNow;
                //     CleanupCache();
                // }

                return result;
            }
            catch (Microsoft.SemanticKernel.HttpOperationException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning(httpEx, 
                    "Rate limit hit on attempt {Attempt}/{MaxRetries}. Retrying in {Delay}s", 
                    attempt + 1, maxRetries, retryDelay.TotalSeconds);
                
                if (attempt < maxRetries - 1)
                {
                    await Task.Delay(retryDelay);
                    retryDelay = retryDelay * 2; // Exponential backoff
                    continue;
                }
                
                return "Xin lỗi, dịch vụ tư vấn đang quá tải. Vui lòng thử lại sau.";
            }
            catch (Microsoft.SemanticKernel.HttpOperationException httpEx)
            {
                _logger.LogError(httpEx, 
                    "HTTP Error calling Gemini - StatusCode: {StatusCode}, Message: {Message}, RequestUri: {RequestUri}", 
                    httpEx.StatusCode, 
                    httpEx.Message,
                    httpEx.InnerException?.Message ?? "N/A");
                return "Xin lỗi, dịch vụ tư vấn tạm thời không khả dụng. Vui lòng thử lại sau.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat response from Gemini: {ExceptionType} - {Message}", 
                    ex.GetType().Name, ex.Message);
                return "Xin lỗi, đã có lỗi xảy ra. Vui lòng thử lại sau.";
            }
        }

        return "Xin lỗi, dịch vụ tư vấn không khả dụng. Vui lòng thử lại sau.";
    }

    private async Task<bool> CheckRateLimitAsync(string identifier)
    {
        var rateLimiter = _rateLimiters.GetOrAdd(identifier, _ => 
            new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10, // 10 requests
                Window = TimeSpan.FromMinutes(1), // per minute
                SegmentsPerWindow = 6, // 6 segments of 10 seconds each
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5 // Allow up to 5 queued requests
            }));

        var lease = await rateLimiter.AcquireAsync(1);
        return lease.IsAcquired;
    }

    private string GenerateCacheKey(string userMessage, int? patientId)
    {
        // Create a simple hash of the message and patient ID for caching
        var patientHash = patientId?.ToString() ?? "no-patient";
        return $"{userMessage.GetHashCode()}_{patientHash}";
    }

    private void CleanupCache()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _cacheTimestamps
            .Where(kvp => now - kvp.Value > _cacheExpiration)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _responseCache.TryRemove(key, out _);
            _cacheTimestamps.TryRemove(key, out _);
        }
    }

    private string BuildSystemPrompt(int? patientId, PatientContextData? contextData)
    {
        var sb = new System.Text.StringBuilder();
        
        sb.AppendLine("Bạn là trợ lý ảo thông minh của Phòng Khám Đa Khoa.");
        sb.AppendLine();
        sb.AppendLine("# QUY TẮC QUAN TRỌNG NHẤT:");
        sb.AppendLine("- KHI GỌI FUNCTION: IM LẶNG HOÀN TOÀN, KHÔNG NÓI GÌ CHO ĐẾN KHI CÓ KẾT QUẢ");
        sb.AppendLine("- CHỈ TRẢ LỜI DUY NHẤT 1 LẦN với thông tin đầy đủ từ kết quả function");
        sb.AppendLine("- TUYỆT ĐỐI KHÔNG nói: Em sẽ kiểm tra, Em sẽ xem, Chờ em nhé");
        sb.AppendLine();
        sb.AppendLine("# CÁCH TRẢ LỜI:");
        sb.AppendLine("- Trả lời NGẮN GỌN (2-3 câu), thân thiện, tự nhiên");
        sb.AppendLine("- Xưng hô: Em (bot) - Anh/Chị (bệnh nhân)");
        sb.AppendLine("- Sử dụng emoji phù hợp");
        sb.AppendLine("- GỌI FUNCTION IM LẶNG → ĐỢI KẾT QUẢ → TRẢ LỜI 1 LẦN");
        sb.AppendLine();
        sb.AppendLine("# KHI NÀO GỌI FUNCTION:");
        sb.AppendLine("- Hỏi về LỊCH HẸN/LỊCH KHÁM → LUÔN gọi LayLichHenSapToiAsync(patientId)");
        sb.AppendLine("- Hỏi về LỊCH SỬ KHÁM → LUÔN gọi LayLichSuKhamAsync(patientId)");
        sb.AppendLine("- Hỏi về KẾT QUẢ XÉT NGHIỆM → LUÔN gọi TraKetQuaXetNghiemAsync(patientId)");
        sb.AppendLine("- Hỏi về BÁC SĨ/CHUYÊN KHOA → LUÔN gọi LayDanhSachBacSiAsync(chuyenKhoa)");
        sb.AppendLine("- Hỏi về DỊCH VỤ Y TẾ/GIÁ KHÁM → LUÔN gọi TimDichVuKhamAsync('dịch vụ')");
        sb.AppendLine("- Hỏi về ĐIỂM TÍCH LŨY → LUÔN gọi LayDiemTichLuyAsync(patientId)");
        sb.AppendLine();
        sb.AppendLine("QUAN TRỌNG: Khi user hỏi về 'dịch vụ y tế', PHẢI gọi TimDichVuKhamAsync với keyword='dịch vụ'");
        sb.AppendLine();
        sb.AppendLine("# VÍ DỤ TRẢ LỜI ĐÚNG:");
        sb.AppendLine("User: tôi có lịch khám nào sắp tới không?");
        sb.AppendLine("Bot: Chào anh! Anh có lịch khám ngày 4/12/2025 lúc 10:30 với BS Quách Minh Phương - Nha khoa. Nhớ đến đúng giờ nhé!");
        sb.AppendLine();
        sb.AppendLine("User: kết quả xét nghiệm của tôi thế nào?");
        sb.AppendLine("Bot: Em thấy anh có kết quả xét nghiệm máu ngày 1/12/2025: Các chỉ số bình thường. Anh yên tâm nhé!");
        sb.AppendLine();
        sb.AppendLine("User: dịch vụ y tế có gì?");
        sb.AppendLine("Bot: Phòng khám có các dịch vụ: Khám tổng quát (200.000đ), Khám chuyên khoa (300.000đ). Anh quan tâm dịch vụ nào ạ?");
        sb.AppendLine();
        sb.AppendLine("# SAI LẦM CẦN TRÁNH:");
        sb.AppendLine("KHÔNG nói: Em sẽ xem lịch hẹn sắp tới của anh nhé");
        sb.AppendLine("KHÔNG nói: Chờ em kiểm tra cho anh");
        sb.AppendLine("CHỈ TRẢ LỜI 1 LẦN với kết quả cụ thể sau khi đã gọi function");

        if (patientId.HasValue)
        {
            sb.AppendLine();
            sb.AppendLine("# THÔNG TIN QUAN TRỌNG:");
            sb.AppendLine($"- Patient ID của bệnh nhân hiện tại: {patientId.Value}");
            sb.AppendLine($"- LUÔN LUÔN sử dụng Patient ID này khi gọi các function");
            sb.AppendLine($"- Ví dụ: LayLichHenSapToiAsync({patientId.Value}), LayDiemTichLuyAsync({patientId.Value})");
        }

        return sb.ToString();
    }

    private string FormatContextData(PatientContextData contextData)
    {
        var sb = new System.Text.StringBuilder();
        
        if (!string.IsNullOrEmpty(contextData.Ten))
        {
            sb.AppendLine($"- Họ tên: {contextData.Ten}");
        }
        
        if (contextData.Tuoi.HasValue)
        {
            sb.AppendLine($"- Tuổi: {contextData.Tuoi}");
        }
        
        if (!string.IsNullOrEmpty(contextData.GioiTinh))
        {
            sb.AppendLine($"- Giới tính: {contextData.GioiTinh}");
        }

        if (contextData.LichHenSapToi != null)
        {
            sb.AppendLine($"\n📅 Lịch hẹn sắp tới:");
            sb.AppendLine($"  - Ngày: {contextData.LichHenSapToi.Ngay} lúc {contextData.LichHenSapToi.Gio}");
            sb.AppendLine($"  - Bác sĩ: {contextData.LichHenSapToi.BacSi}");
            sb.AppendLine($"  - Chuyên khoa: {contextData.LichHenSapToi.ChuyenKhoa}");
        }

        if (contextData.LichSuKham?.Any() == true)
        {
            sb.AppendLine($"\n🏥 Lịch sử khám gần nhất:");
            var recentVisits = contextData.LichSuKham.Take(3);
            foreach (var visit in recentVisits)
            {
                sb.AppendLine($"  - {visit.Ngay}: {visit.ChuyenKhoa} - BS {visit.BacSi}");
                if (!string.IsNullOrEmpty(visit.ChanDoan))
                {
                    sb.AppendLine($"    Chẩn đoán: {visit.ChanDoan}");
                }
            }
        }

        if (contextData.ChuyenKhoaCuaPhongKham?.Any() == true)
        {
            sb.AppendLine($"\n🏥 Chuyên khoa có tại phòng khám:");
            sb.AppendLine($"  {string.Join(", ", contextData.ChuyenKhoaCuaPhongKham)}");
        }

        return sb.ToString();
    }
}

// Data model cho context
public class PatientContextData
{
    public string? Ten { get; set; }
    public int? Tuoi { get; set; }
    public string? GioiTinh { get; set; }
    public List<LichSuKhamItem>? LichSuKham { get; set; }
    public LichHenSapToiItem? LichHenSapToi { get; set; }
    public List<string>? ChuyenKhoaCuaPhongKham { get; set; }
    public List<KetQuaXetNghiemItem>? KetQuaXetNghiem { get; set; }
    public List<LichSuThanhToanItem>? LichSuThanhToan { get; set; }
}

public class LichSuKhamItem
{
    public string? Ngay { get; set; }
    public string? BacSi { get; set; }
    public string? ChuyenKhoa { get; set; }
    public string? ChanDoan { get; set; }
    public string? DieuTri { get; set; }
}

public class LichHenSapToiItem
{
    public string? Ngay { get; set; }
    public string? Gio { get; set; }
    public string? BacSi { get; set; }
    public string? ChuyenKhoa { get; set; }
}

public class KetQuaXetNghiemItem
{
    public string? Ngay { get; set; }
    public string? ChiTiet { get; set; }
}

public class LichSuThanhToanItem
{
    public string? Ngay { get; set; }
    public decimal SoTien { get; set; }
    public string? PhuongThuc { get; set; }
    public string? TrangThai { get; set; }
}
