namespace SourceModule
{
    using Microsoft.Azure.Devices.Shared;

    public class DesiredPropertiesData
    {
        private int _dataLength = 1024;
        private int _fieldLength = 100;
        private int _rowCount = 100;
        private int _interval = 180; // 3 min

        public int DataLength => _dataLength;
        public int FieldLength => _fieldLength;
        public int RowCount => _rowCount;
        public int Interval => _interval;

        public DesiredPropertiesData(TwinCollection twinCollection)
        {
            if (twinCollection.Contains("DataLength") && twinCollection["DataLength"] != null)
            {
                _dataLength = twinCollection["DataLength"];
            }
            if (twinCollection.Contains("FieldLength") && twinCollection["FieldLength"] != null)
            {
                _fieldLength = twinCollection["FieldLength"];
            }
            if (twinCollection.Contains("RowCount") && twinCollection["RowCount"] != null)
            {
                _rowCount = twinCollection["RowCount"];
            }
            if (twinCollection.Contains("Interval") && twinCollection["Interval"] != null)
            {
                _interval = twinCollection["Interval"];
            }
        }
    }
}