using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _logDirectory = "logs";
    private static readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
    private static readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

    public RequestResponseLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
        Directory.CreateDirectory(_logDirectory);
    }

    public async Task Invoke(HttpContext context)
    {
        var logEntry = new StringBuilder();

        // Логируем информацию о запросе
        logEntry.AppendLine($"[{DateTime.Now:HH:mm:ss:fff}] {context.Request.Method} {context.Request.Path}");

        // Логирование тела запроса
        context.Request.EnableBuffering();
        var requestBody = await new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true).ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(requestBody))
        {
            logEntry.AppendLine($"{new string(' ', 18)}   запрос Body:");
            logEntry.AppendLine(FormatJsonBody(requestBody));
        }

        // Копируем оригинальный поток ответа
        var originalResponseBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        // Проверяем, нужно ли логировать тело ответа
        bool shouldLogResponseBody = !(context.Request.Method == "GET" &&
                                     context.Request.Path.StartsWithSegments("/hs/Tander/exchange") &&
                                     context.Response.StatusCode >= 200);

        // Логирование информации о ответе
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBodyText = shouldLogResponseBody
            ? await new StreamReader(context.Response.Body).ReadToEndAsync()
            : "[тело ответа пропущено]";
        context.Response.Body.Seek(0, SeekOrigin.Begin);

        logEntry.AppendLine($"{new string(' ', 18)} ответ Status: {context.Response.StatusCode}");

        if (shouldLogResponseBody && !string.IsNullOrWhiteSpace(responseBodyText))
        {
            logEntry.AppendLine($"{new string(' ', 18)} ответ Body:");
            logEntry.AppendLine(FormatJsonBody(responseBodyText));
        }
        else if (!shouldLogResponseBody)
        {
            logEntry.AppendLine($"{new string(' ', 18)} ответ Body: [пропущено для GET /hs/Tander/exchange]");
        }

        // Добавляем лог в очередь и пробуем записать
        _logQueue.Enqueue(logEntry.ToString());
        await TryWriteLogsAsync();

        await responseBody.CopyToAsync(originalResponseBodyStream);
    }

    private async Task TryWriteLogsAsync()
    {
        if (_writeLock.CurrentCount > 0 && !_logQueue.IsEmpty)
        {
            await _writeLock.WaitAsync();
            try
            {
                var logFilePath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
                var sb = new StringBuilder();

                while (_logQueue.TryDequeue(out var logEntry))
                {
                    sb.Append(logEntry);
                }

                if (sb.Length > 0)
                {
                    await File.AppendAllTextAsync(logFilePath, sb.ToString());
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    private string FormatJsonBody(string json)
    {
        try
        {
            var formatted = new StringBuilder();
            using (var reader = new StringReader(json))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    formatted.AppendLine($"{new string(' ', 21)}{line}");
                }
            }
            return formatted.ToString();
        }
        catch
        {
            return $"{new string(' ', 21)}{json}";
        }
    }
}