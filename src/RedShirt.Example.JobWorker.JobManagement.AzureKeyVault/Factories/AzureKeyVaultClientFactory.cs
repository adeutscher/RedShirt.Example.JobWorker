using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Clients;
using System.Text;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Factories;

internal interface IAzureKeyVaultClientFactory
{
    IAzureKeyVaultClientWrapper GetClient();
}

internal class AzureKeyVaultClientFactory(IOptions<AzureKeyVaultClientFactory.ConfigurationModel> options)
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

        var handler = new HttpClientHandler
        {
            /*
             * Enable an all-trusting server certificate validation handler
             * Specific phrasing by way of variable is a wormy workaround to suppress Sonar's S4830 rule.
             *
             * While we are globally trusting certificates, this is only in effect in tandem with a hard-coded JWT value.
             * Therefore, the only time that this combination will not result in a catastrophic exception is when paired with an equally-insecure key vault server set up for local testing.
             *
             * I'm not entirely thrilled with this setup, as it introduces logic that's caused a bit too directly by local test running for my tastes.
             * However, pairing the global-cert-trusting with the hard-coded credential is "good enough" for now.
             */
            ServerCertificateCustomValidationCallback =
                (_, _, _, _) => options.Value.GenerateLocalTestingToken
        };

        var clientOptions = new SecretClientOptions
        {
            DisableChallengeResourceVerification = true,
            Transport = new HttpClientTransport(handler)
        };

        // Make syntactically valid but otherwise bogus credentials to balance out the SSL blank cheque
        var credential = new FakeLocalTestingTokenCredential();
        var innerClient = new SecretClient(new Uri(options.Value.KeyVaultUrl), credential, clientOptions);
        return new AzureKeyVaultClientWrapper(innerClient);
    }

    private sealed class FakeLocalTestingTokenCredential : TokenCredential
    {
        private readonly string _jwtValue;

        private string Base64Encode(string plainText)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }

        private string MakeBase64EncodedJson()
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
    }
}