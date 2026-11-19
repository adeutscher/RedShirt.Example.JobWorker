using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Clients;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;

internal interface IAzureKeyVaultClientFactory
{
    IAzureKeyVaultClientWrapper GetClient();
}

internal sealed class AzureKeyVaultClientFactory(
    IOptions<AzureKeyVaultClientFactory.ConfigurationModel> options,
    ILogger<AzureKeyVaultClientFactory> logger)
    : IAzureKeyVaultClientFactory
{
    public IAzureKeyVaultClientWrapper GetClient()
    {
        if (!options.Value.GenerateLocalTestingToken)
        {
            var liveCredential = new DefaultAzureCredential();
            var liveClient = new SecretClient(new Uri(options.Value.KeyVaultUrl), liveCredential);
            return new AzureKeyVaultClientWrapper(liveClient);
        }

        /*
         * Local testing mode, for a Key Vault emulator with a self-signed certificate.
         *
         * Rather than trusting every certificate, trust only the emulator's own certificate, loaded from a file.
         * This applies to this one HttpClient and leaves the system trust store alone. Hostname checks stay on.
         * Pairing a bogus token with a trust-all handler kept real credentials from leaking, but it still let any
         * server that could intercept the connection hand the worker forged secrets. A pinned certificate closes that.
         */
        var caCertificatePath = options.Value.LocalTestingCaCertificatePath;
        if (string.IsNullOrWhiteSpace(caCertificatePath))
        {
            throw new InvalidOperationException(
                "Azure Key Vault local testing mode requires LocalTestingCaCertificatePath to be set.");
        }

        logger.LogWarning(
            "Azure Key Vault local testing mode is enabled. A bogus access token is sent, and only certificates " +
            "issued by {CaCertificatePath} are trusted. Do not use this mode outside of local testing.",
            caCertificatePath);

        var caCertificate = X509CertificateLoader.LoadCertificateFromFile(caCertificatePath);
        var clientOptions = new SecretClientOptions
        {
            DisableChallengeResourceVerification = true,
            Transport = new HttpClientTransport(CreateLocalTestingHandler(caCertificate))
        };

        // Syntactically valid but otherwise bogus credentials, accepted only by a local testing emulator.
        var credential = new FakeLocalTestingTokenCredential();
        var innerClient = new SecretClient(new Uri(options.Value.KeyVaultUrl), credential, clientOptions);
        return new AzureKeyVaultClientWrapper(innerClient);
    }

    /// <summary>
    ///     Build an <see cref="HttpClientHandler" /> that trusts server certificates chaining to
    ///     <paramref name="caCertificate" />, in addition to the system trust store.
    /// </summary>
    internal static HttpClientHandler CreateLocalTestingHandler(X509Certificate2 caCertificate)
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                IsTrustedByLocalTestingCa(caCertificate, certificate, errors)
        };
    }

    /// <summary>
    ///     Server certificate check for local testing mode.
    ///     A certificate that the system already trusts passes. Otherwise, only an untrusted chain is forgiven, and only
    ///     when the chain builds to <paramref name="caCertificate" />. A hostname mismatch or a missing certificate always
    ///     fails.
    /// </summary>
    internal static bool IsTrustedByLocalTestingCa(X509Certificate2 caCertificate, X509Certificate2? certificate,
        SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || certificate is null)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
        // A local emulator certificate has no revocation endpoint to check.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }

    private sealed class FakeLocalTestingTokenCredential : TokenCredential
    {
        private readonly string _jwtValue;

        private static string Base64Encode(string plainText)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }

        private static string MakeBase64EncodedJson()
        {
            return Base64Encode(JsonSerializer.Serialize(new {A = "b"}));
        }

        private AccessToken GetTokenInner()
        {
            return new AccessToken(_jwtValue, DateTimeOffset.UtcNow.AddDays(1));
        }

        public FakeLocalTestingTokenCredential()
        {
            _jwtValue = $"{MakeBase64EncodedJson()}.{MakeBase64EncodedJson()}.{Base64Encode("1")}";
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return GetTokenInner();
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            return new ValueTask<AccessToken>(GetTokenInner());
        }
    }

    internal sealed class ConfigurationModel
    {
        public required string KeyVaultUrl { get; init; }
        public required bool GenerateLocalTestingToken { get; init; }

        /// <summary>
        ///     Path to the PEM or DER certificate of the local Key Vault emulator. Required when
        ///     <see cref="GenerateLocalTestingToken" /> is <c>true</c>, and ignored otherwise.
        /// </summary>
        public string? LocalTestingCaCertificatePath { get; init; }

        /// <summary>
        ///     Startup check: local testing mode must name a CA certificate file that exists, so that the emulator's
        ///     certificate is pinned instead of every certificate being trusted.
        /// </summary>
        internal static bool IsValid(ConfigurationModel model)
        {
            return !model.GenerateLocalTestingToken
                   || (!string.IsNullOrWhiteSpace(model.LocalTestingCaCertificatePath)
                       && File.Exists(model.LocalTestingCaCertificatePath));
        }
    }
}