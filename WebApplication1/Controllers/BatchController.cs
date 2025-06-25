using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WebApplication1.Controllers.Classes;

namespace WebApplication1.Controllers
{
    //ПЕРЕДАЧА ОТВЕТА НА ПЛОЩАДКУ С НОМЕРОМ ОТРАБОТАННОЙ ПАРТИИ
    [ApiController]
    [Route("[controller]")]
    public class BatchController : ControllerBase
    {
        private readonly BatchSettings _settings;
        private readonly AuthSettings _auth;

        public BatchController(IOptions<BatchSettings> settings, IOptions<AuthSettings> auth)
        {
            _settings = settings.Value;
            _auth = auth.Value;
        }

        [HttpPost("acknowledge-batch")]
        public IActionResult AcknowledgeBatch(
            [FromBody] JsonElement body,
            [FromQuery] string login,
            [FromQuery] string password)
        {
            if (login?.Trim() != _auth.Login || password?.Trim() != _auth.Password)
            {
                return Unauthorized(new { IsSuccess = false, message = "Неверный логин или пароль." });
            }
            
            try
            {
                List<string> batchIds = new();

                if (!body.TryGetProperty("BatchIds", out var batchIdsElement))
                {
                    return BadRequest(new { IsSuccess = false, message = "Не найден параметр BatchIds." });
                }

                if (batchIdsElement.ValueKind == JsonValueKind.String)
                {
                    batchIds.Add(batchIdsElement.GetString());
                }
                else if (batchIdsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var id in batchIdsElement.EnumerateArray())
                        batchIds.Add(id.GetString());
                }
                else
                {
                    return BadRequest(new { IsSuccess = false, message = "Неверный формат BatchIds. Должна быть строка или массив строк." });
                }

                var sourcePath = Path.GetFullPath(_settings.SourceDirectory);
                var archivePath = Path.GetFullPath(_settings.ArchiveDirectory);

                if (!Directory.Exists(sourcePath))
                    return StatusCode(500, new { IsSuccess = false, message = "Исходная папка не найдена." });

                Directory.CreateDirectory(archivePath);

                var files = Directory.GetFiles(sourcePath, "*.json");
                int movedCount = 0;

                foreach (var file in files)
                {
                    var nameParts = Path.GetFileNameWithoutExtension(file).Split(' ');
                    if (nameParts.Length < 3) continue;

                    var batchIdFromName = nameParts[2];
                    if (batchIds.Contains(batchIdFromName))
                    {
                        var destPath = Path.Combine(archivePath, Path.GetFileName(file));
                        System.IO.File.Move(file, destPath, overwrite: true);
                        movedCount++;
                    }
                }

                return Ok(new { IsSuccess = true, message = "" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { IsSuccess = false, message = ex.Message });
            }
        }
    }

}
