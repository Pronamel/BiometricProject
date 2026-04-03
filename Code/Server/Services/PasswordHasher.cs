using System;
using Isopoh.Cryptography.Argon2;

namespace Server.Services;

public static class PasswordHasher
{
    public static string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty", nameof(password));
        }

        return Argon2.Hash(password);
    }

    public static bool VerifyPassword(string? storedHash, string password)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        try
        {
            return Argon2.Verify(storedHash, password);
        }
        catch
        {
            return false;
        }
    }
}
