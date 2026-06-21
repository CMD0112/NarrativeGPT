using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class ContinuityWarningDismissalService
{
    public static string HashMessage(string message)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(message.Trim()));
        return Convert.ToHexString(bytes);
    }

    public static bool IsDismissed(ContinuityDocument continuity, string message)
    {
        if (continuity.DismissedWarningHashes.Count == 0)
            return false;

        var hash = HashMessage(message);
        return continuity.DismissedWarningHashes.Contains(hash, StringComparer.OrdinalIgnoreCase);
    }

    public static void Dismiss(ContinuityDocument continuity, string message)
    {
        var hash = HashMessage(message);
        if (!continuity.DismissedWarningHashes.Contains(hash, StringComparer.OrdinalIgnoreCase))
            continuity.DismissedWarningHashes.Add(hash);
    }

    public static List<ContinuityWarningEntry> FilterActive(ContinuityDocument continuity) =>
        continuity.Warnings
            .Where(w => !IsDismissed(continuity, w.Message))
            .ToList();
}
