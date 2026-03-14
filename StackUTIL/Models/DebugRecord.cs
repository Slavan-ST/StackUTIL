namespace DebugInterceptor.Models
{
    public class DebugRecord
    {
        public long RowId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string RawMatch { get; set; } = string.Empty;
        public string GeneratedQuery { get; set; } = string.Empty;

        public override string ToString() => $"{RowId}: {TableName}";
    }
}