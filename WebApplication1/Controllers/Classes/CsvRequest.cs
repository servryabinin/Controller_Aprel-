namespace WebApplication1.Controllers.Classes
{
    public class CsvRequest
    {
        public string ProductionSiteId { get; set; }
        public string ProductTypeId { get; set; }
        public List<string> Items { get; set; }
    }

}
