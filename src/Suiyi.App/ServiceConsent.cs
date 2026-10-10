using System;
using System.Linq;
using Suiyi.App.Platform;

namespace Suiyi.App;

/// <summary>Remembers a user's confirmation for a complete destination, independent of model or credentials.</summary>
public static class ServiceConsent
{
    public static string NormalizeEndpoint(string endpoint) => CredentialStore.NormalizeEndpoint(endpoint);

    public static bool IsAccepted(AppSettings settings, string endpoint)
    {
        try
        {
            string normalized = NormalizeEndpoint(endpoint);
            return settings.AcceptedServiceEndpoints.Any(saved =>
            {
                try { return string.Equals(NormalizeEndpoint(saved), normalized, StringComparison.Ordinal); }
                catch (ArgumentException) { return false; }
            });
        }
        catch (ArgumentException) { return false; }
    }

    public static void Accept(AppSettings settings, string endpoint)
    {
        string normalized = NormalizeEndpoint(endpoint);
        if (IsAccepted(settings, normalized)) return;
        if (settings.AcceptedServiceEndpoints.Count >= 256) settings.AcceptedServiceEndpoints.RemoveAt(0);
        settings.AcceptedServiceEndpoints.Add(normalized);
    }
}
