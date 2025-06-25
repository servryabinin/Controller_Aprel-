using Newtonsoft.Json;

public class BatchRequest
{
    [JsonProperty("BatchIds")]
    public object BatchIds { get; set; } // Может быть string или List<string>
}