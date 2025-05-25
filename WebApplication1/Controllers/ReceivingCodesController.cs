using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using WebApplication1.Controllers.Classes;

namespace WebApplication1.Controllers
{
    //ПЕРЕДАЧА КМ НА ПЛОЩАДКУ
    [ApiController]
    [Route("[controller]")]
    public class ReceivingCodesController : ControllerBase
    {
        private readonly CsvSettings _csvSettings;
        private readonly AuthSettings _auth;

        public ReceivingCodesController(IOptions<CsvSettings> csvSettings, IOptions<AuthSettings> auth)
        {
            _csvSettings = csvSettings.Value;
            _auth = auth.Value;
        }

        [HttpPost("generate-csv")]
        public IActionResult GenerateCsv(
            [FromQuery] string login,
            [FromQuery] string password,
            [FromBody] CsvRequest request)
        {
            if (login?.Trim() != _auth.Login || password?.Trim() != _auth.Password)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "Неверный логин или пароль."
                });
            }

            if (request == null || request.Items == null || string.IsNullOrEmpty(request.ProductTypeId))
            {
                return BadRequest("Invalid request: убедитесь, что передан ProductTypeId и Items.");
            }

            try
            {
                string basePath = _csvSettings.OutputDirectory;

                if (!Directory.Exists(basePath))
                    Directory.CreateDirectory(basePath);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var fileName = $"{request.ProductTypeId}_{timestamp}.csv";
                var fullPath = Path.Combine(basePath, fileName);

                var csv = new StringBuilder();
                foreach (var item in request.Items)
                    csv.AppendLine(item);

                System.IO.File.WriteAllText(fullPath, csv.ToString(), Encoding.UTF8);

                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка при создании CSV: {ex.Message}"
                });
            }
        }
    }
}
