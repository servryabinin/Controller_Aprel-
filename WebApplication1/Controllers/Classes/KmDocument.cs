namespace WebApplication1.Controllers.Classes
{
    public class KmDocument
    {
        public string BatchID { get; set; }
        public Guid ProductionSiteId { get; set; }
        public DateOnly ExpirationDate { get; set; }
        public DateTime ProductionDate { get; set; }
        public string ProductTypeId { get; set; }
        public List<string> Items { get; set; }
    }

}
