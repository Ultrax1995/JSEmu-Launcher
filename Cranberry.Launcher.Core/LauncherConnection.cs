using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Cranberry.Launcher.Core;

public static class LauncherConnection
{
    public static bool ValidateCertificate(X509Certificate? certificate, SslPolicyErrors errors, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return errors == SslPolicyErrors.None;
        return certificate is not null && pin.Length == 64 && pin.All(char.IsAsciiHexDigit)
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()), Convert.FromHexString(pin));
    }

    public static HttpClient CreateHttp(LauncherSettings settings)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
            ValidateCertificate(cert, errors, settings.CertificateSha256);
        return new HttpClient(handler) { BaseAddress = LauncherSettings.ValidateServer(settings.ServerUrl), Timeout = TimeSpan.FromMinutes(20) };
    }
}
