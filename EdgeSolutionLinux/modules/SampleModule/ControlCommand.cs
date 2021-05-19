namespace SampleModule
{
    using Newtonsoft.Json;

    public enum ControlCommandEnum
    {
        Reset = 0,
        Noop = 1
    };

    public class ControlCommand
    {
        [JsonProperty("command")]
        public ControlCommandEnum Command { get; set; }
    }
}