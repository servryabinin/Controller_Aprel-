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

        // ����� ����� �������� �����������
        private IActionResult CheckAuth()
        {
            // 1. ��������� ������� ��������� Authorization
            if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                return Unauthorized(new
                {
                    Code = "AUTH_HEADER_MISSING",
                    Message = "��������� Authorization �����������",
                    Solution = "�������� ���������: 'Authorization: Basic {base64(�����:������)}'"
                });
            }

            // 2. ��������� ������ ���������
            var authValue = authHeader.ToString();
            if (!authValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                return Unauthorized(new
                {
                    Code = "INVALID_AUTH_SCHEME",
                    Message = "���������������� ����� �����������",
                    Solution = "����������� Basic Auth: 'Authorization: Basic {credentials}'",
                    ReceivedHeader = authValue[..10] + (authValue.Length > 10 ? "..." : "") // ������ ������ 10 ��������
                });
            }

            try
            {
                // 3. ���������� credentials
                var encodedCredentials = authValue["Basic ".Length..].Trim();
                var decodedBytes = Convert.FromBase64String(encodedCredentials);
                var credentials = Encoding.UTF8.GetString(decodedBytes).Split(':', 2);

                // 4. ��������� ������ credentials
                if (credentials.Length != 2)
                {
                    Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                    return Unauthorized(new
                    {
                        Code = "INVALID_CREDENTIALS_FORMAT",
                        Message = "�������� ������ ������� ������",
                        Solution = "����������� ������ '�����:������' ����� ������������ � base64",
                        DecodedSample = credentials.Length == 0 ? "null" : credentials[0]
                    });
                }

                // 5. ��������� �����/������
                var username = credentials[0].Trim();
                var password = credentials[1].Trim();

                if (username != _auth.Login || password != _auth.Password)
                {
                    Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                    return Unauthorized(new
                    {
                        Code = "INVALID_CREDENTIALS",
                        Message = "�������� ����� ��� ������",
                        Solution = "��������� ������� ������",
                        //Hint = $"��������� �����: {_auth.Login?.First() + new string('*', _auth.Login?.Length - 1 ?? 0)}"
                    });
                }

                return null; // �������� �����������
            }
            catch (FormatException)
            {
                Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                return Unauthorized(new
                {
                    Code = "INVALID_BASE64",
                    Message = "������������ base64 ������",
                    Solution = "��������� ��������� credentials"
                });
            }
            catch (Exception ex)
            {
                Response.Headers["WWW-Authenticate"] = "Basic realm=\"TanderAPI\"";
                return Unauthorized(new
                {
                    Code = "AUTH_PROCESSING_ERROR",
                    Message = "������ ��������� �����������",
                    Details = ex.GetType().Name,
                    Solution = "��������� ������ ��� ���������� � ��������������"
                });
            }
        }

        // ������ ������ (exchange) - POST ����� (������ GenerateCsv)
        [HttpPost("exchange")]
        public IActionResult ExchangePost(
            //[FromQuery] string login,
            //[FromQuery] string password,
            [FromBody] CsvRequest request
            )
        {
            // �������� �����������
            //if (login?.Trim() != _auth.Login || password?.Trim() != _auth.Password)
            //{
            //    return Unauthorized(new
            //    {
            //        IsSuccess = false,
            //        message = "�������� ����� ��� ������."
            //    });
            //}
            var authResult = CheckAuth();
            if (authResult != null) return authResult;

            // �������� �� ���������� ���� �������
            if (request == null)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = "����������� ���� �������."
                });
            }

            // �������� ������������ �����
            if (string.IsNullOrEmpty(request.ProductionSiteId))
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = "����������� ProductionSiteId."
                });
            }

            if (string.IsNullOrEmpty(request.ProductTypeId))
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = "����������� ProductTypeId."
                });
            }

            if (request.Items == null || request.Items.Count == 0)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = "����������� �������� � ������ Items."
                });
            }

            // �������������� �������� �� ���������� ����� ����������
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = "��������� ������ ��� ���������� � ������ Items."
                    });
                }
            }

            try
            {
                string basePath = _csvSettings.OutputDirectory;

                // �������� � �������� ����������
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
                            message = $"������ ��� �������� ����������: {ex.Message}"
                        });
                    }
                }

                var timestamp = DateTime.Now.ToString("dd.MM.yyyy_HH.mm.ss", CultureInfo.InvariantCulture);
                var fileName = $"{request.ProductTypeId}_{timestamp}.csv";
                var fullPath = Path.Combine(basePath, fileName);

                // �������� �� ������������� ����� (�� ������, ���� timestamp ��������)
                if (System.IO.File.Exists(fullPath))
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = "���� � ����� ������ ��� ����������."
                    });
                }

                var csv = new StringBuilder();
                foreach (var item in request.Items)
                    csv.AppendLine(item);

                // ������ ����� � ���������� ��������� ������
                try
                {
                    System.IO.File.WriteAllText(fullPath, csv.ToString(), Encoding.UTF8);
                }
                catch (UnauthorizedAccessException)
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = "����������� ����� �� ������ � ��������� ����������."
                    });
                }
                catch (IOException ioEx)
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = $"������ �����-������: {ioEx.Message}"
                    });
                }

                // �������� �����
                return Ok(new
                {
                    IsSuccess = true,
                    message = "000000001" // ����� ��������� �� 1�
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = $"����������� ������: {ex.Message}"
                });
            }
        }

        // ������ ������ (exchange) - GET ����� (������ ExportKms)
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
            //        Message = "�������� ����� ��� ������."
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
                    return BadRequest($"������ ��� ��������� ����� \"{Path.GetFileName(file)}\": {ex.GetType().Name} - {ex.Message}");
                }
            }

            return Ok(result);
        }

        // ������ result - POST ����� (������ AcknowledgeBatch)
        [HttpPost("result")]
        public IActionResult ResultPost(
            [FromBody] BatchRequest request
            //[FromQuery] string login,
            //[FromQuery] string password
            )
        {
            // 1. �������� �����������
            //if (string.IsNullOrWhiteSpace(login))
            //{
            //    return Unauthorized(new { IsSuccess = false, message = "����� �� ������." });
            //}

            //if (string.IsNullOrWhiteSpace(password))
            //{
            //    return Unauthorized(new { IsSuccess = false, message = "������ �� ������." });
            //}

            //if (login.Trim() != _auth.Login || password.Trim() != _auth.Password)
            //{
            //    return Unauthorized(new { IsSuccess = false, message = "�������� ����� ��� ������." });
            //}

            var authResult = CheckAuth();
            if (authResult != null) return authResult;

            if (request == null)
            {
                return StatusCode(500, new { IsSuccess = false, message = "���� ������� �����������." });
            }

            // 3. �������� � ������������ BatchIds
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
                        message = $"���������������� ������ BatchIds. ��������� ������ ��� ������ �����."
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = $"������ ��������� BatchIds: {ex.Message}"
                });
            }

            if (batchIds.Count == 0)
            {
                return StatusCode(500, new { IsSuccess = false, message = "BatchIds �� �������� ���������� ��������." });
            }

            // 4. ������ � �������� �������� (�������� ��� ���������)
            try
            {
                var sourcePath = Path.GetFullPath(_batchSettings.SourceDirectory);
                var archivePath = Path.GetFullPath(_batchSettings.ArchiveDirectory);

                if (!Directory.Exists(sourcePath))
                {
                    return StatusCode(500, new { IsSuccess = false, message = $"�������� ���������� �� �������: {sourcePath}" });
                }

                try
                {
                    Directory.CreateDirectory(archivePath);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { IsSuccess = false, message = $"�� ������� ������� �������� ����������: {ex.Message}" });
                }

                var files = Directory.GetFiles(sourcePath, "*.json");
                if (files.Length == 0)
                {
                    return StatusCode(500, new { IsSuccess = false, message = "� �������� ���������� �� ������� ����� ��� ���������." });
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
                                errors.Add($"������ ����������� ����� {fileName}: {ioEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"������ ��������� ����� {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                if (errors.Count > 0)
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = $"���������� ������: {processedFiles}. ������: {string.Join("; ", errors)}"
                    });
                }

                if (processedFiles == 0)
                {
                    return StatusCode(500, new
                    {
                        IsSuccess = false,
                        message = "�� ������� ������ � ���������� BatchIds."
                    });
                }

                return Ok(new
                {
                    IsSuccess = true,
                    message = $"������� ���������� ������: {processedFiles}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    message = $"����������� ������: {ex.Message}"
                });
            }
        }
    }
}