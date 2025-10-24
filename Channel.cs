namespace MyApp.Models
{
    public class MessageItem
    {
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public string Content { get; set; } = string.Empty;
    }
}


using System.Threading.Channels;
using MyApp.Models;

namespace MyApp.Services
{
    public class MessageChannelService
    {
        private readonly Channel<MessageItem> _channel;

        public MessageChannelService()
        {
            // Unbounded: 제한 없는 채널 (BoundedChannelOptions 로 제한도 가능)
            _channel = Channel.CreateUnbounded<MessageItem>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,  // 로그 서비스 하나만 읽음
                    SingleWriter = false  // 여러 스레드(API)에서 동시에 씀
                });
        }

        public ChannelWriter<MessageItem> Writer => _channel.Writer;
        public ChannelReader<MessageItem> Reader => _channel.Reader;

        public ValueTask WriteAsync(MessageItem item, CancellationToken token = default)
            => _channel.Writer.WriteAsync(item, token);
    }
}


using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyApp.Models;

namespace MyApp.Services
{
    public class MessageLogWorker : BackgroundService
    {
        private readonly ChannelReader<MessageItem> _reader;
        private readonly ILogger<MessageLogWorker> _logger;
        private readonly string _logFilePath;

        public MessageLogWorker(MessageChannelService service, ILogger<MessageLogWorker> logger)
        {
            _reader = service.Reader;
            _logger = logger;
            _logFilePath = Path.Combine(AppContext.BaseDirectory, "messages.log");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MessageLogWorker started.");

            var buffer = new List<MessageItem>();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 일정 시간동안 메시지 수집
                    var start = DateTime.UtcNow;
                    buffer.Clear();

                    while ((DateTime.UtcNow - start).TotalSeconds < 5 &&
                           await _reader.WaitToReadAsync(stoppingToken))
                    {
                        while (_reader.TryRead(out var item))
                        {
                            buffer.Add(item);
                        }

                        // 약간의 delay로 CPU 과점 방지
                        await Task.Delay(100, stoppingToken);
                    }

                    if (buffer.Count > 0)
                    {
                        await WriteToFileAsync(buffer);
                        _logger.LogInformation("Logged {Count} messages.", buffer.Count);
                    }
                    else
                    {
                        _logger.LogDebug("No messages to log.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in MessageLogWorker loop.");
                    await Task.Delay(2000, stoppingToken);
                }
            }
        }

        private async Task WriteToFileAsync(IEnumerable<MessageItem> messages)
        {
            using var writer = new StreamWriter(_logFilePath, append: true);
            foreach (var msg in messages)
            {
                await writer.WriteLineAsync($"[{msg.ReceivedAt:O}] {msg.Content}");
            }
        }
    }
}



using Microsoft.AspNetCore.Mvc;
using MyApp.Models;
using MyApp.Services;

namespace MyApp.Controllers
{
    [ApiController]
    [Route("api/messages")]
    public class MessageController : ControllerBase
    {
        private readonly MessageChannelService _channelService;
        private readonly ILogger<MessageController> _logger;

        public MessageController(MessageChannelService channelService, ILogger<MessageController> logger)
        {
            _channelService = channelService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> PostAsync([FromBody] string message, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(message))
                return BadRequest("Message cannot be empty.");

            var item = new MessageItem { Content = message };
            await _channelService.WriteAsync(item, token);

            _logger.LogInformation("Message received: {Message}", message);
            return Ok(new { status = "queued" });
        }
    }
}




using MyApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<MessageChannelService>();
builder.Services.AddHostedService<MessageLogWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.Run();
