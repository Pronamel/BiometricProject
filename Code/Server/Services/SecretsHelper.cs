using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System.Text.Json;

namespace Server.Services;

public class SecretsHelper
{
    private static string? _cachedSecret = null;
    private static string? _cachedSdiHmacSecret = null;

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
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ JWT secret loaded from AWS Secrets Manager");
            return _cachedSecret;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Failed to load JWT secret from AWS Secrets Manager: {ex.Message}");
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

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ SDI HMAC secret loaded from AWS Secrets Manager ({secretId})");
            return _cachedSdiHmacSecret;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Failed to load SDI HMAC secret from AWS Secrets Manager: {ex.Message}");
            throw;
        }
    }
}
