namespace ProjectTraiding.CustomFeatures.Options;

public class CalendarSourceOptions
{
    public string IssBaseUrl { get; set; } = "https://iss.moex.com/iss";

    public string ApimBaseUrl { get; set; } = "https://apim.moex.com/iss";

    public string AlgKey { get; set; } = string.Empty;

    public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxConnectionsPerServer { get; set; } = 10;

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan TotalRequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public string CertificatesDirectory { get; set; } = "Certificates";

    public CalendarRevocationPolicy CertificateRevocationPolicy { get; set; } = CalendarRevocationPolicy.Off;
}
