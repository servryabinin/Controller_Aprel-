using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using WebApplication1.Controllers.Classes;

namespace WebApplication1.Controllers
{
    //ПОЛУЧЕНИЕ КМ ДЛЯ ВВОДА В ОБОРОТ
    [ApiController]
    [Route("[controller]")]
    public class KmsExportController : ControllerBase
    {
        private readonly BatchSettings _settings;
        private readonly AuthSettings _auth;

        public KmsExportController(IOptions<BatchSettings> settings, IOptions<AuthSettings> auth)
        {
            _settings = settings.Value;
            _auth = auth.Value;
        }

        [HttpGet("export-kms")]
        public IActionResult ExportKms([FromQuery] string login, [FromQuery] string password)
        {
            if (login?.Trim() != _auth.Login || password?.Trim() != _auth.Password)
            {
                return Unauthorized(new
                {
                    IsSuccess = false,
                    Message = "Неверный логин или пароль."
                });
            }

            var productionSiteId = _settings.ProductionSiteID;
            var sourcePath = Path.GetFullPath(_settings.SourceDirectory);
            if (!Directory.Exists(sourcePath))
                return NotFound("Source directory not found.");

            var result = new List<object>();
            var files = Directory.GetFiles(sourcePath, "*.json");
            int processedCount = 0;

            foreach (var file in files)
            {
                try
                {
                    using var stream = System.IO.File.OpenRead(file);
                    using var doc = JsonDocument.Parse(stream);

                    var root = doc.RootElement;

                    string? expirationDate = root.GetProperty("ExpirationDate").GetString();
                    string? productionDate = root.GetProperty("DateProduct").GetString();
                    string? unitGTIN = root.GetProperty("UnitGTIN").GetString();

                    var items = new List<string>();
                    if (root.TryGetProperty("UnitPackList", out var unitPackList))
                    {
                        foreach (var unit in unitPackList.EnumerateArray())
                        {
                            if (unit.TryGetProperty("code", out var codeElement))
                                items.Add(codeElement.GetString());
                        }
                    }


                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var batchId = fileName.Split(' ').Last();

                    var document = new KmDocument
                    {
                        BatchID = batchId,
                        ProductionSiteId = Guid.Parse(productionSiteId),
                        ExpirationDate = DateOnly.ParseExact(expirationDate!, "dd.MM.yyyy"),
                        ProductionDate = DateTime.ParseExact(productionDate!, "dd.MM.yyyy", CultureInfo.InvariantCulture),
                        ProductTypeId = unitGTIN,
                        Items = items
                    };

                    result.Add(document);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    return BadRequest($"Ошибка при обработке файла \"{Path.GetFileName(file)}\": {ex.GetType().Name} - {ex.Message}");
                }
            }

            return Ok(new
            {
                result
            });
        }
    }
}
