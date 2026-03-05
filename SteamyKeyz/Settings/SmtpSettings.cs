namespace SteamyKeyz.Settings;

public class SmtpSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool EnableSsl { get; set; }
    public string FromAddress { get; set; } = "noreply@steamykeyz.local";
    public string FromName { get; set; } = "SteamyKeyz";
}