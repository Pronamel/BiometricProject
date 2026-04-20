using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System.Text.Json;

namespace Server.Services;

public class SecretsHelper
{
    private static string? _cachedSecret = null;
    private static string? _cachedSdiHmacSecret = null;
    private static string? _cachedVoterEncryptionPublicKey = null;
    private static string? _cachedVoterEncryptionPrivateKey = null;

    public static async Task<string> GetJWTSecret()
    {
        if (_cachedSecret != null) return _cachedSecret;

        try
        {
            var client = new AmazonSecretsManagerClient(RegionEndpoint.USEast1);

            var request = new GetSecretValueRequest
            {
                SecretId = "jwt-secret"
            };

            var response = await client.GetSecretValueAsync(request);
            var secretJson = JsonSerializer.Deserialize<Dictionary<string, string>>(response.SecretString);
            
            if (secretJson == null || !secretJson.ContainsKey("JWT_SECRET"))
                throw new InvalidOperationException("JWT_SECRET key not found in secret json");
            
            _cachedSecret = secretJson["JWT_SECRET"];
            
            if (string.IsNullOrWhiteSpace(_cachedSecret))
                throw new InvalidOperationException("JWT_SECRET is empty");
            
            if (_cachedSecret.Length < 32)
                throw new InvalidOperationException("JWT_SECRET must be at least 32 bytes long");
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] JWT secret loaded from AWS Secrets Manager");
            return _cachedSecret;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to load JWT secret from AWS Secrets Manager: {ex.Message}");
            throw;
        }
    }

    public static async Task<string> GetSdiHmacSecret()
    {
        if (_cachedSdiHmacSecret != null) return _cachedSdiHmacSecret;

        try
        {
            var client = new AmazonSecretsManagerClient(RegionEndpoint.USEast1);
            var secretId = Environment.GetEnvironmentVariable("SDI_HMAC_SECRET_ID") ?? "securevote/prod/sdi-hmac-key";

            var request = new GetSecretValueRequest
            {
                SecretId = secretId
            };

            var response = await client.GetSecretValueAsync(request);
            var secretString = response.SecretString;

            if (string.IsNullOrWhiteSpace(secretString))
                throw new InvalidOperationException("SDI HMAC secret is empty");

            // Support both raw plaintext secrets and JSON key/value secrets.
            try
            {
                var secretJson = JsonSerializer.Deserialize<Dictionary<string, string>>(secretString);
                if (secretJson != null)
                {
                    if (secretJson.TryGetValue("SDI_HMAC_KEY", out var keyedValue) && !string.IsNullOrWhiteSpace(keyedValue))
                    {
                        _cachedSdiHmacSecret = keyedValue;
                    }
                    else if (secretJson.TryGetValue("value", out var rawValue) && !string.IsNullOrWhiteSpace(rawValue))
                    {
                        _cachedSdiHmacSecret = rawValue;
                    }
                }
            }
            catch
            {
                // Secret is not JSON - handled below as plaintext.
            }

            _cachedSdiHmacSecret ??= secretString;

            if (string.IsNullOrWhiteSpace(_cachedSdiHmacSecret))
                throw new InvalidOperationException("Resolved SDI HMAC secret is empty");

            if (_cachedSdiHmacSecret.Length < 32)
                throw new InvalidOperationException("SDI HMAC secret must be at least 32 characters long");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] SDI HMAC secret loaded from AWS Secrets Manager ({secretId})");
            return _cachedSdiHmacSecret;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to load SDI HMAC secret from AWS Secrets Manager: {ex.Message}");
            throw;
        }
    }

    public static async Task<string> GetVoterEncryptionPublicKeyPem()
    {
        if (_cachedVoterEncryptionPublicKey != null) return _cachedVoterEncryptionPublicKey;

        try
        {
            var client = new AmazonSecretsManagerClient(RegionEndpoint.USEast1);
            const string secretId = "securevote/prod/voter-encryption-public-key";

            var request = new GetSecretValueRequest
            {
                SecretId = secretId
            };

            var response = await client.GetSecretValueAsync(request);
            var secretString = response.SecretString;

            if (string.IsNullOrWhiteSpace(secretString))
                throw new InvalidOperationException("Voter encryption public key secret is empty");

            try
            {
                var secretJson = JsonSerializer.Deserialize<Dictionary<string, string>>(secretString);
                if (secretJson != null)
                {
                    if (secretJson.TryGetValue("PUBLIC_KEY_PEM", out var pemValue) && !string.IsNullOrWhiteSpace(pemValue))
                    {
                        _cachedVoterEncryptionPublicKey = pemValue;
                    }
                    else if (secretJson.TryGetValue("VOTER_ENCRYPTION_PUBLIC_KEY_PEM", out var voterPemValue) && !string.IsNullOrWhiteSpace(voterPemValue))
                    {
                        _cachedVoterEncryptionPublicKey = voterPemValue;
                    }
                    else if (secretJson.TryGetValue("RSA_PUBLIC_KEY_PEM", out var rsaPemValue) && !string.IsNullOrWhiteSpace(rsaPemValue))
                    {
                        _cachedVoterEncryptionPublicKey = rsaPemValue;
                    }
                    else if (secretJson.TryGetValue("value", out var rawValue) && !string.IsNullOrWhiteSpace(rawValue))
                    {
                        _cachedVoterEncryptionPublicKey = rawValue;
                    }
                }
            }
            catch
            {
                // Secret is not JSON - handled below as plaintext PEM.
            }

            _cachedVoterEncryptionPublicKey ??= secretString;

            if (string.IsNullOrWhiteSpace(_cachedVoterEncryptionPublicKey))
                throw new InvalidOperationException("Resolved voter encryption public key is empty");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter encryption public key loaded from AWS Secrets Manager ({secretId})");
            return _cachedVoterEncryptionPublicKey;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to load voter encryption public key from AWS Secrets Manager: {ex.Message}");
            throw;
        }
    }

    public static async Task<string> GetVoterEncryptionPrivateKeyPem()
    {
        if (_cachedVoterEncryptionPrivateKey != null) return _cachedVoterEncryptionPrivateKey;

        // Allow direct PEM injection for emergency/runtime scenarios.
        var directPem = Environment.GetEnvironmentVariable("VOTER_ENCRYPTION_PRIVATE_KEY_PEM");
        if (!string.IsNullOrWhiteSpace(directPem))
        {
            _cachedVoterEncryptionPrivateKey = directPem;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter encryption private key loaded from environment variable (VOTER_ENCRYPTION_PRIVATE_KEY_PEM)");
            return _cachedVoterEncryptionPrivateKey;
        }

        try
        {
            var client = new AmazonSecretsManagerClient(RegionEndpoint.USEast1);
            const string secretId = "securevote/prod/voter-encryption-private-key";

            var request = new GetSecretValueRequest
            {
                SecretId = secretId
            };

            var response = await client.GetSecretValueAsync(request);
            var secretString = response.SecretString;

            if (string.IsNullOrWhiteSpace(secretString))
                throw new InvalidOperationException("Voter encryption private key secret is empty");

            try
            {
                var secretJson = JsonSerializer.Deserialize<Dictionary<string, string>>(secretString);
                if (secretJson != null)
                {
                    if (secretJson.TryGetValue("PRIVATE_KEY_PEM", out var pemValue) && !string.IsNullOrWhiteSpace(pemValue))
                    {
                        _cachedVoterEncryptionPrivateKey = pemValue;
                    }
                    else if (secretJson.TryGetValue("VOTER_ENCRYPTION_PRIVATE_KEY_PEM", out var voterPemValue) && !string.IsNullOrWhiteSpace(voterPemValue))
                    {
                        _cachedVoterEncryptionPrivateKey = voterPemValue;
                    }
                    else if (secretJson.TryGetValue("RSA_PRIVATE_KEY_PEM", out var rsaPemValue) && !string.IsNullOrWhiteSpace(rsaPemValue))
                    {
                        _cachedVoterEncryptionPrivateKey = rsaPemValue;
                    }
                    else if (secretJson.TryGetValue("value", out var rawValue) && !string.IsNullOrWhiteSpace(rawValue))
                    {
                        _cachedVoterEncryptionPrivateKey = rawValue;
                    }
                }
            }
            catch
            {
                // Secret is not JSON - handled below as plaintext PEM.
            }

            _cachedVoterEncryptionPrivateKey ??= secretString;

            if (string.IsNullOrWhiteSpace(_cachedVoterEncryptionPrivateKey))
                throw new InvalidOperationException("Resolved voter encryption private key is empty");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter encryption private key loaded from AWS Secrets Manager ({secretId})");
            return _cachedVoterEncryptionPrivateKey;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to load voter encryption private key from AWS Secrets Manager: {ex.Message}");
            throw;
        }
    }
}
