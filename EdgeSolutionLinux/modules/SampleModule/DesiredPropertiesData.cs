namespace SampleModule
{
    using System;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;

    public class DesiredPropertiesData
    {
        private bool _sendData = true;
        private int _sendInterval = 30;
        private int _dataLength = 1024;

        public bool SendData => _sendData;
        public int SendInterval => _sendInterval;
        public int DataLength => _dataLength;

        public DesiredPropertiesData(TwinCollection twinCollection)
        {
            Console.WriteLine($"Updating desired properties {twinCollection.ToJson(Formatting.Indented)}");
            try
            {
                if (twinCollection.Contains("SendData") && twinCollection["SendData"] != null)
                {
                    _sendData = twinCollection["SendData"];
                }
                if (twinCollection.Contains("SendInterval") && twinCollection["SendInterval"] != null)
                {
                    _sendInterval = twinCollection["SendInterval"];
                }
                if (twinCollection.Contains("DataLength") && twinCollection["DataLength"] != null)
                {
                    _dataLength = twinCollection["DataLength"];
                }
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Error while processing desired property: {exception}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"Error while processing desired property: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"SendData = {_sendData}");
                Console.WriteLine($"SendInterval = {_sendInterval}");
                Console.WriteLine($"DataLength = {_dataLength}");
            }
        }
    }
}