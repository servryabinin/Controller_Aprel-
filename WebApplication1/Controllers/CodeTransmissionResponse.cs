using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WebApplication1.Controllers.Classes;

namespace WebApplication1.Controllers
{
    //����� �� �������� ����� �� ������� ���������������� ��������
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
                    Message = "�������� ����� ��� ������."
                });
            }

            // �������� �� null ��� ������ �������� � ������������ �����
            if (request == null)
            {
                return StatusCode(500, new CsvResponse
                {
                    IsSuccess = false,
                    Message = "����������� ���� �������."
                });
            }

            if (string.IsNullOrEmpty(request.ProductionSiteId))
            {
                return StatusCode(500, new CsvResponse
                {
                    IsSuccess = false,
                    Message = "����������� ProductionSiteId."
                });
            }

            if (string.IsNullOrEmpty(request.ProductTypeId))
            {
                return StatusCode(500, new CsvResponse
                {
                    IsSuccess = false,
                    Message = "����������� ProductTypeId."
                });
            }

            if (request.Items == null || request.Items.Count == 0)
            {
                return StatusCode(500, new CsvResponse
                {
                    IsSuccess = false,
                    Message = "����������� �������� � ������ Items."
                });
            }

            // ���� ��� ���� ���������, ���������� �������� �����
            return Ok(new CsvResponse
            {
                IsSuccess = true,
                Message = "000000001" // ����� �������� �� ����� ��������� �� 1�
            });
        }
    }
}
