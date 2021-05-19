namespace SampleModule
{
    using Newtonsoft.Json;

    public class MessageBody
    {
        [JsonProperty("machine")]
        public Machine Machine { get; set; }
        [JsonProperty("ambient")]
        public Ambient Ambient { get; set; }
        [JsonProperty("timeCreated")]
        public string TimeCreated { get; set; }
        [JsonProperty("bin")]
        public byte[] Bin { get; set; }
    }

    [JsonObject("machine")]
    public class Machine
    {
        [JsonProperty("temperature")]
        public double Temperature { get; set; }
        [JsonProperty("pressure")]
        public double Pressure { get; set; }
    }

    [JsonObject("ambient")]
    public class Ambient
    {
        [JsonProperty("temperature")]
        public double Temperature { get; set; }
        [JsonProperty("humidity")]
        public int Humidity { get; set; }
    }
}