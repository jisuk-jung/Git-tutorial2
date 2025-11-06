{
  "LoggingConfig": {
    "LogDirectory": "logs",
    "MaxFileSizeMB": 5,
    "BackupCount": 3
  }
}

namespace FastApiLogApp.Services
{
    public class LogConfig
    {
        public string LogDirectory { get; set; } = "logs";
        public int MaxFileSizeMB { get; set; } = 5;
        public int BackupCount { get; set; } = 3;
    }
}

using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Options;

namespace FastApiLogApp.Services
{
    public class AsyncRotatingLogger
    {
        private readonly LogConfig _config;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public AsyncRotatingLogger(IOptions<LogConfig> config)
        {
            _config = config.Value;
            Directory.CreateDirectory(_config.LogDirectory);
        }

        private string GetDailyLogPath()
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            return Path.Combine(_config.LogDirectory, $"{today}.log");
        }

        private void RotateLogs(string logPath)
        {
            var maxSizeBytes = _config.MaxFileSizeMB * 1024 * 1024;
            var fileInfo = new FileInfo(logPath);
            if (!fileInfo.Exists || fileInfo.Length < maxSizeBytes)
                return;

            for (int i = _config.BackupCount; i >= 1; i--)
            {
                var src = $"{logPath}.{i}";
                var dest = $"{logPath}.{i + 1}";
                if (File.Exists(src))
                {
                    if (i == _config.BackupCount)
                        File.Delete(src);
                    else
                        File.Move(src, dest, true);
                }
            }

            File.Move(logPath, $"{logPath}.1", true);
        }

        public async Task WriteLogAsync(object data)
        {
            var logPath = GetDailyLogPath();
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            var logLine = $"{DateTime.Now:O} - {json}{Environment.NewLine}";

            await _lock.WaitAsync(); // 비동기 락 (여러 요청 간 동기화)
            try
            {
                RotateLogs(logPath);
                await File.AppendAllTextAsync(logPath, logLine, Encoding.UTF8);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using FastApiLogApp.Services;

namespace FastApiLogApp.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LogController : ControllerBase
    {
        private readonly AsyncRotatingLogger _logger;

        public LogController(AsyncRotatingLogger logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] object body)
        {
            await _logger.WriteLogAsync(body);
            return Ok(new { status = "ok" });
        }
    }
}

using FastApiLogApp.Services;

var builder = WebApplication.CreateBuilder(args);

// 환경설정 바인딩
builder.Services.Configure<LogConfig>(
    builder.Configuration.GetSection("LoggingConfig"));

// 로그 서비스 등록
builder.Services.AddSingleton<AsyncRotatingLogger>();

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
