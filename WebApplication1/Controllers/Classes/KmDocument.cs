using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebApplication1.Controllers.Classes
{
    public class KmDocument
    {
        [JsonPropertyName("BatchID")]
        public string BatchID { get; set; }

        [JsonPropertyName("ProductionSiteId")]
        public Guid ProductionSiteId { get; set; }

        [JsonPropertyName("ExpirationDate")]
        public DateOnly ExpirationDate { get; set; }

        [JsonPropertyName("ProductionDate")]
        public DateOnly ProductionDate { get; set; }

        [JsonPropertyName("ProductTypeId")]
        public string ProductTypeId { get; set; }

        [JsonPropertyName("Items")]
        public List<string> Items { get; set; }
    }
}