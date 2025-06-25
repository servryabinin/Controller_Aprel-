
using WebApplication1.Controllers.Classes;

namespace WebApplication1
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Добавляем сервисы
            builder.Services.Configure<CsvSettings>(builder.Configuration.GetSection("CsvSettings"));
            builder.Services.Configure<BatchSettings>(builder.Configuration.GetSection("BatchSettings"));
            builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));

            // Настройки Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Configure(builder.Configuration.GetSection("Kestrel"));
            });

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Используем middlewares
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseMiddleware<RequestResponseLoggingMiddleware>(); // Добавляем наш логгер
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
