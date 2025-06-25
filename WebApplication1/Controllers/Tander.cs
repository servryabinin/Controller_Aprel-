using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;
using System.Text.Json;
using WebApplication1.Controllers.Classes;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("hs/Tander")]
    public class TanderController : ControllerBase
    {
        private readonly CsvSettings _csvSettings;
        private readonly BatchSettings _batchSettings;
        private readonly AuthSettings _auth;

        public TanderController(
            IOptions<CsvSettings> csvSettings,
            IOptions<BatchSettings> batchSettings,
            IOptions<AuthSettings> auth)
        {
            _csvSettings = csvSettings.Value;
            _batchSettings = batchSettings.Value;
            _auth = auth.Value;
        }

        // Общий метод проверки авторизации
        private IActionResult CheckAuth()
        {
            // 1. Проверяем наличие заголовка Authorization
            if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                return Unauthorized(new
                {
                    Code = "AUTH_HEADER_MISSING",
                    Message = "Заголовок Authorization отсутствует",
                    Solution = "Добавьте заголовок: 'Authorization: Basic {base64(логин:пароль)}'"
                });
            }

            // 2. Проверяем формат заголовка
            var authValue = authHeader.ToString();
            if (!authValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                return Unauthorized(new
                {
                    Code = "INVALID_AUTH_SCHEME",
                    Message = "Неподдерживаемая схема авторизации",
                    Solution = "Используйте Basic Auth: 'Authorization: Basic {credentials}'",
                    ReceivedHeader = authValue[..10] + (authValue.Length > 10 ? "..." : "") // Пример первых 10 символов
                });
            }

            try
            {
                // 3. Декодируем credentials
                var encodedCredentials = authValue["Basic ".Length..].Trim();
                var decodedBytes = Convert.FromBase64String(encodedCredentials);
                var credentials = Encoding.UTF8.GetString(decodedBytes).Split(':', 2);

                // 4. Проверяем формат credentials
                if (credentials.Length != 2)
                {
                    Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                    return Unauthorized(new
                    {
                        Code = "INVALID_CREDENTIALS_FORMAT",
                        Message = "Неверный формат учетных данных",
                        Solution = "Используйте формат 'логин:пароль' перед кодированием в base64",
                        DecodedSample = credentials.Length == 0 ? "null" : credentials[0]
                    });
                }

                // 5. Проверяем логин/пароль
                var username = credentials[0].Trim();
                var password = credentials[1].Trim();

                if (username != _auth.Login || password != _auth.Password)
                {
                    Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                    return Unauthorized(new
                    {
                        Code = "INVALID_CREDENTIALS",
                        Message = "Неверный логин или пароль",
                        Solution = "Проверьте учетные данные",
                        //Hint = $"Ожидаемый логин: {_auth.Login?.First() + new string('*', _auth.Login?.Length - 1 ?? 0)}"
                    });
                }

                return null; // Успешная авторизация
            }
            catch (FormatException)
            {
                Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                return Unauthorized(new
                {
                    Code = "INVALID_BASE64",
                    Message = "Некорректный base64 формат",
                    Solution = "Проверьте кодировку credentials"
                });
            }
            catch (Exception ex)
            {
                Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                return Unauthorized(new
                {
                    Code = "AUTH_PROCESSING_ERROR",
                    Message = "Ошибка обработки авторизации",
                    Details = ex.GetType().Name,
                    Solution = "Повторите запрос или обратитесь к администратору"
                });
            }
        }

        // Ресурс обмена (exchange) - POST метод (аналог GenerateCsv)
        [HttpPost("exchange")]
        public IActionResult ExchangePost(
            //[FromQuery] string login,
            //[FromQuery] string password,
            [FromBody] CsvRequest request
            )
        {
            // Проверка авторизации
            //if (login?.Trim() != _auth.Login || password?.Trim() != _auth.Password)
            //{
            //    return Unauthorized(new
            //    {
            //        IsSuccess = false,
            //        message = "Неверный логин или пароль."
            //    });
            //}
            var authResult = CheckAuth();
            if (authResult != null) return authResult;

            // Проверка на отсутствие тела запроса
            if (request == null)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = "Отсутствует тело запроса."
                });
            }

            // Проверка обязательных полей
            if (string.IsNullOrEmpty(request.ProductionSiteId))
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = "Отсутствует ProductionSiteId."
                });
            }

            if (string.IsNullOrEmpty(request.ProductTypeId))
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = "Отсутствует ProductTypeId."
                });
            }

            if (request.Items == null || request.Items.Count == 0)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = "Отсутствуют элементы в списке Items."
                });
            }

            // Дополнительная проверка на валидность кодов маркировки
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = "Обнаружен пустой код маркировки в списке Items."
                    });
                }
            }

            try
            {
                string basePath = _csvSettings.OutputDirectory;

                // Проверка и создание директории
                if (!Directory.Exists(basePath))
                {
                    try
                    {
                        Directory.CreateDirectory(basePath);
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new
                        {
                            IsSuccess = false,
                            message = $"Ошибка при создании директории: {ex.Message}"
                        });
                    }
                }

                var timestamp = DateTime.Now.ToString("dd.MM.yyyy_HH.mm.ss", CultureInfo.InvariantCulture);
                var fileName = $"{request.ProductTypeId}_{timestamp}.csv";
                var fullPath = Path.Combine(basePath, fileName);

                // Проверка на существование файла (на случай, если timestamp совпадет)
                if (System.IO.File.Exists(fullPath))
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = "Файл с таким именем уже существует."
                    });
                }

                var csv = new StringBuilder();
                foreach (var item in request.Items)
                    csv.AppendLine(item);

                // Запись файла с обработкой возможных ошибок
                try
                {
                    System.IO.File.WriteAllText(fullPath, csv.ToString(), Encoding.UTF8);
                }
                catch (UnauthorizedAccessException)
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = "Отсутствуют права на запись в указанную директорию."
                    });
                }
                catch (IOException ioEx)
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = $"Ошибка ввода-вывода: {ioEx.Message}"
                    });
                }

                // Успешный ответ
                return Ok(new
                {
                    IsSuccess = true,
                    message = "000000001" // Номер документа из 1С
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = $"Неожиданная ошибка: {ex.Message}"
                });
            }
        }

        // Ресурс обмена (exchange) - GET метод (аналог ExportKms)
        [HttpGet("exchange")]
        public IActionResult ExchangeGet(
            //[FromQuery] string login, 
            //[FromQuery] string password
            )
        {
            //if (login?.Trim() != _auth.Login || password?.Trim() != _auth.Password)
            //{
            //    return Unauthorized(new
            //    {
            //        IsSuccess = false,
            //        Message = "Неверный логин или пароль."
            //    });
            //}

            var authResult = CheckAuth();
            if (authResult != null) return authResult;

            var productionSiteId = _batchSettings.ProductionSiteID;
            var sourcePath = Path.GetFullPath(_batchSettings.SourceDirectory);
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
                        ProductionDate = DateOnly.ParseExact(productionDate!, "dd.MM.yyyy"),
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

            return Ok(result);
        }

        // Ресурс result - POST метод (аналог AcknowledgeBatch)
        [HttpPost("result")]
        public IActionResult ResultPost(
            [FromBody] BatchRequest request
            //[FromQuery] string login,
            //[FromQuery] string password
            )
        {
            // 1. Проверка авторизации
            //if (string.IsNullOrWhiteSpace(login))
            //{
            //    return Unauthorized(new { IsSuccess = false, message = "Логин не указан." });
            //}

            //if (string.IsNullOrWhiteSpace(password))
            //{
            //    return Unauthorized(new { IsSuccess = false, message = "Пароль не указан." });
            //}

            //if (login.Trim() != _auth.Login || password.Trim() != _auth.Password)
            //{
            //    return Unauthorized(new { IsSuccess = false, message = "Неверный логин или пароль." });
            //}

            var authResult = CheckAuth();
            if (authResult != null) return authResult;

            if (request == null)
            {
                return StatusCode(500, new { IsSuccess = false, message = "Тело запроса отсутствует." });
            }

            // 3. Проверка и нормализация BatchIds
            List<string> batchIds = new List<string>();
            var jsElem = (JsonElement)request.BatchIds;

            try
            {
                if (jsElem.ValueKind == JsonValueKind.String)
                {
                    var str = jsElem.GetString();
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        batchIds.Add(str);
                    }
                }
                else if (jsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in jsElem.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String)
                        {
                            var str = element.GetString();
                            if (!string.IsNullOrWhiteSpace(str))
                            {
                                batchIds.Add(str);
                            }
                        }
                    }
                }
                else
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = $"Неподдерживаемый формат BatchIds. Ожидается строка или массив строк."
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = $"Ошибка обработки BatchIds: {ex.Message}"
                });
            }

            if (batchIds.Count == 0)
            {
                return StatusCode(500, new { IsSuccess = false, message = "BatchIds не содержит допустимых значений." });
            }

            // 4. Работа с файловой системой (осталось без изменений)
            try
            {
                var sourcePath = Path.GetFullPath(_batchSettings.SourceDirectory);
                var archivePath = Path.GetFullPath(_batchSettings.ArchiveDirectory);

                if (!Directory.Exists(sourcePath))
                {
                    return StatusCode(500, new { IsSuccess = false, message = $"Исходная директория не найдена: {sourcePath}" });
                }

                try
                {
                    Directory.CreateDirectory(archivePath);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { IsSuccess = false, message = $"Не удалось создать архивную директорию: {ex.Message}" });
                }

                var files = Directory.GetFiles(sourcePath, "*.json");
                if (files.Length == 0)
                {
                    return StatusCode(500, new { IsSuccess = false, message = "В исходной директории не найдены файлы для обработки." });
                }

                int processedFiles = 0;
                var errors = new List<string>();

                foreach (var file in files)
                {
                    try
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var nameParts = fileName.Split(' ');

                        if (nameParts.Length < 3) continue;

                        var batchIdFromFile = nameParts[2];
                        if (batchIds.Contains(batchIdFromFile))
                        {
                            var destPath = Path.Combine(archivePath, Path.GetFileName(file));
                            try
                            {
                                System.IO.File.Move(file, destPath, overwrite: true);
                                processedFiles++;
                            }
                            catch (IOException ioEx)
                            {
                                errors.Add($"Ошибка перемещения файла {fileName}: {ioEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Ошибка обработки файла {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                if (errors.Count > 0)
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = $"Обработано файлов: {processedFiles}. Ошибки: {string.Join("; ", errors)}"
                    });
                }

                if (processedFiles == 0)
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = "Не найдено файлов с указанными BatchIds."
                    });
                }

                return Ok(new
                {
                    IsSuccess = true,
                    message = $"Успешно обработано файлов: {processedFiles}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = $"Критическая ошибка: {ex.Message}"
                });
            }
        }
    }
}