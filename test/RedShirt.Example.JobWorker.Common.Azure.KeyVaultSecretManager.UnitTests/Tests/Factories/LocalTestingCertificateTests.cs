using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.UnitTests.Tests.Factories;

/// <summary>
///     Checks for local testing mode certificate pinning, including real TLS handshakes against an in-process server.
/// </summary>
public class LocalTestingCertificateTests
{
    private static X509Certificate2 CreateCa(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static CertificateRequest CreateServerRequest(ECDsa key, string dnsName, bool isCa)
    {
        var request = new CertificateRequest("CN=azure-key-vault-emulator", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(isCa, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], false));
        return request;
    }

    private static X509Certificate2 CreateServerCertificate(X509Certificate2 ca, string dnsName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = CreateServerRequest(key, dnsName, false);
        var serial = RandomNumberGenerator.GetBytes(16);
        using var certificate = request.Create(ca, DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddDays(7), serial);
        return certificate.CopyWithPrivateKey(key);
    }

    /// <summary>
    ///     Self-signed server certificate that is also its own CA, like the one made by generate-azure-key-vault-cert.sh.
    /// </summary>
    private static X509Certificate2 CreateSelfSignedServerCertificate(string dnsName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return CreateServerRequest(key, dnsName, true)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(7));
    }

    private static X509Certificate2 PublicOnly(X509Certificate2 certificate)
    {
        return X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
    }

    /// <summary>
    ///     Serve one HTTPS request on loopback with <paramref name="serverCertificate" />, and request it through the
    ///     local testing handler pinned to <paramref name="pinnedCa" />.
    /// </summary>
    private static async Task<bool> RequestSucceedsAsync(X509Certificate2 serverCertificate,
        X509Certificate2 pinnedCa, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var ssl = new SslStream(client.GetStream());
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate
                }, cancellationToken);

                var buffer = new byte[4096];
                var received = new StringBuilder();
                while (!received.ToString().Contains("\r\n\r\n"))
                {
                    var read = await ssl.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        return;
                    }

                    received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                await ssl.WriteAsync(
                    "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(),
                    cancellationToken);
            }
            catch (Exception e) when (e is AuthenticationException or IOException)
            {
                // The client rejected the certificate and closed the connection.
            }
        }, cancellationToken);

        try
        {
            using var handler = AzureKeyVaultClientFactory.CreateLocalTestingHandler(pinnedCa);
            using var httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            try
            {
                var body = await httpClient.GetStringAsync($"https://localhost:{port}/", cancellationToken);
                return body == "ok";
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }
        finally
        {
            listener.Stop();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    [Fact]
    public async Task Handshake_CertificateFromPinnedCa_Succeeds()
    {
        using var ca = CreateCa("Pinned CA");
        using var server = CreateServerCertificate(ca, "localhost");
        using var pinned = PublicOnly(ca);

        Assert.True(await RequestSucceedsAsync(server, pinned, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handshake_SelfSignedCertificatePinnedDirectly_Succeeds()
    {
        using var server = CreateSelfSignedServerCertificate("localhost");
        using var pinned = PublicOnly(server);

        Assert.True(await RequestSucceedsAsync(server, pinned, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handshake_CertificateFromOtherCa_Fails()
    {
        using var pinnedCa = CreateCa("Pinned CA");
        using var otherCa = CreateCa("Attacker CA");
        using var server = CreateServerCertificate(otherCa, "localhost");
        using var pinned = PublicOnly(pinnedCa);

        Assert.False(await RequestSucceedsAsync(server, pinned, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handshake_HostnameMismatch_FailsEvenFromPinnedCa()
    {
        using var ca = CreateCa("Pinned CA");
        using var server = CreateServerCertificate(ca, "not-localhost.example");
        using var pinned = PublicOnly(ca);

        Assert.False(await RequestSucceedsAsync(server, pinned, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void IsTrustedByLocalTestingCa_NoErrors_Trusted()
    {
        using var ca = CreateCa("Pinned CA");

        Assert.True(AzureKeyVaultClientFactory.IsTrustedByLocalTestingCa(ca, null, SslPolicyErrors.None));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch)]
    public void IsTrustedByLocalTestingCa_OtherErrors_NotTrusted(SslPolicyErrors errors)
    {
        using var ca = CreateCa("Pinned CA");
        using var server = CreateServerCertificate(ca, "localhost");

        Assert.False(AzureKeyVaultClientFactory.IsTrustedByLocalTestingCa(ca, server, errors));
    }

    [Fact]
    public void IsTrustedByLocalTestingCa_ChainErrorsWithoutCertificate_NotTrusted()
    {
        using var ca = CreateCa("Pinned CA");

        Assert.False(AzureKeyVaultClientFactory.IsTrustedByLocalTestingCa(ca, null,
            SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void IsTrustedByLocalTestingCa_ChainErrors_TrustedOnlyForPinnedCa()
    {
        using var ca = CreateCa("Pinned CA");
        using var otherCa = CreateCa("Attacker CA");
        using var fromPinned = CreateServerCertificate(ca, "localhost");
        using var fromOther = CreateServerCertificate(otherCa, "localhost");

        Assert.True(AzureKeyVaultClientFactory.IsTrustedByLocalTestingCa(ca, fromPinned,
            SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(AzureKeyVaultClientFactory.IsTrustedByLocalTestingCa(ca, fromOther,
            SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Theory]
    [InlineData(false, null, false, true)]
    [InlineData(false, "", false, true)]
    [InlineData(true, null, false, false)]
    [InlineData(true, " ", false, false)]
    [InlineData(true, "missing", false, false)]
    [InlineData(true, "existing", true, true)]
    public void ConfigurationModel_IsValid(bool localTesting, string? pathKind, bool createFile, bool expected)
    {
        var path = pathKind switch
        {
            "missing" => Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.crt"),
            "existing" => Path.GetTempFileName(),
            _ => pathKind
        };

        try
        {
            if (createFile)
            {
                Assert.True(File.Exists(path));
            }

            var model = new AzureKeyVaultClientFactory.ConfigurationModel
            {
                KeyVaultUrl = "https://localhost:4997",
                GenerateLocalTestingToken = localTesting,
                LocalTestingCaCertificatePath = path
            };

            Assert.Equal(expected, AzureKeyVaultClientFactory.ConfigurationModel.IsValid(model));
        }
        finally
        {
            if (pathKind == "existing" && path is not null)
            {
                File.Delete(path);
            }
        }
    }
}
