using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System.Text.Json;

namespace Server.Services;

public class SecretsHelper
{
    private static string? _cachedSecret = null;

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
}
