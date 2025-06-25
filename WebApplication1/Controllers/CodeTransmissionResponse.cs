using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WebApplication1.Controllers.Classes;

namespace WebApplication1.Controllers
{
    //ОТВЕТ НА ПЕРЕДАЧУ КОДОВ СО СТОРОНЫ ПРОИЗВОДСТВЕННОЙ ПЛОЩАДКИ
    [ApiController]
    [Route("[controller]")]
    public class CodeTransmissionResponseController : ControllerBase
    {
        private readonly AuthSettings _auth;

        public CodeTransmissionResponseController(IOptions<AuthSettings> auth)
        {
            _auth = auth.Value;
        }

        [HttpPost("process-request")]
        public IActionResult ProcessRequest(
            [FromBody] CsvRequest request,
            [FromQuery] string login,
            [FromQuery] string password)
        {
            if (login?.Trim() != _auth.Login || password?.Trim() != _auth.Password)
            {
                return Unauthorized(new CsvResponse
                {
                    IsSuccess = false,
                    Message = "Неверный логин или пароль."
                });
            }

            // Проверка на null или пустые значения в обязательных полях
            if (request == null)
            {
                return StatusCode(500, new CsvResponse
                {
                    IsSuccess = false,
                    Message = "Отсутствует тело запроса."
                });
            }

            if (string.IsNullOrEmpty(request.ProductionSiteId))
            {
                return StatusCode(500, new CsvResponse
                {
                    IsSuccess = false,
                    Message = "Отсутствует ProductionSiteId."
                });
            }

            if (string.IsNullOrEmpty(request.ProductTypeId))
            {
                return StatusCode(500, new CsvResponse
                {
                    IsSuccess = false,
                    Message = "Отсутствует ProductTypeId."
                });
            }

            if (request.Items == null || request.Items.Count == 0)
            {
                return StatusCode(500, new CsvResponse
                {
                    IsSuccess = false,
                    Message = "Отсутствуют элементы в списке Items."
                });
            }

            // Если все поля корректны, возвращаем успешный ответ
            return Ok(new CsvResponse
            {
                IsSuccess = true,
                Message = "000000001" // Можно заменить на номер документа из 1С
            });
        }
    }
}
