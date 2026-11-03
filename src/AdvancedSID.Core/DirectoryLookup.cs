using System.Collections.Concurrent;
using System.DirectoryServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace AdvancedSID.Core;

/// <summary>
/// Resolves SIDs to accounts. Uses the Windows LSA (SID -> DOMAIN\user, works across trusts)
/// and then LDAP for display name, enabled state and last logon.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DirectoryLookup
{
    private static readonly string[] Properties =
    {
        "displayName", "userAccountControl", "lastLogonTimestamp", "mail", "distinguishedName",
    };

    private readonly ConcurrentDictionary<string, DirectoryUser> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<bool> domainJoined = new(DetectDomain);

    /// <summary>False when this PC is not in a domain - unresolved SIDs are then "unknown", not "orphaned".</summary>
    public bool IsDomainJoined => domainJoined.Value;

    public void ClearCache() => cache.Clear();

    public DirectoryUser Lookup(string sid) => cache.GetOrAdd(sid, LookupCore);

    public void LookupAll(IEnumerable<UserRecord> users, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var list = users.Where(u => u.Sid is not null).ToList();
        var done = 0;
        Parallel.ForEach(
            list,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            user =>
            {
                user.Directory = Lookup(user.Sid!);
                var n = Interlocked.Increment(ref done);
                if (n % 10 == 0 || n == list.Count)
                {
                    progress?.Report(new ScanProgress($"Looking up users in Active Directory ({n}/{list.Count})...", n, list.Count));
                }
            });
    }

    private DirectoryUser LookupCore(string sid)
    {
        var user = new DirectoryUser();
        SecurityIdentifier identifier;
        try
        {
            identifier = new SecurityIdentifier(sid);
        }
        catch (Exception ex)
        {
            user.State = DirectoryLookupState.Error;
            user.Error = "Invalid SID: " + ex.Message;
            return user;
        }

        try
        {
            user.AccountName = ((NTAccount)identifier.Translate(typeof(NTAccount))).Value;
            user.State = DirectoryLookupState.Found;
        }
        catch (IdentityNotMappedException)
        {
            if (IsDomainJoined)
            {
                user.State = DirectoryLookupState.NotFound;
                user.Error = "SID does not resolve - account deleted or from an untrusted domain.";
            }
            else
            {
                user.State = DirectoryLookupState.Error;
                user.Error = "This PC is not domain joined, so the SID could not be resolved.";
            }

            return user;
        }
        catch (Exception ex)
        {
            user.State = DirectoryLookupState.Error;
            user.Error = ex.Message;
            return user;
        }

        if (!IsDomainJoined)
        {
            return user;
        }

        try
        {
            // Bind straight to the object by SID; works for the current domain.
            using var searcher = new DirectorySearcher($"(objectSid={sid})", Properties) { ClientTimeout = TimeSpan.FromSeconds(15) };
            var result = searcher.FindOne();
            if (result is null)
            {
                // Probably in another domain of the forest - try the global catalog.
                using var gc = new DirectoryEntry("GC:");
                foreach (DirectoryEntry root in gc.Children)
                {
                    using (root)
                    using (var gcSearcher = new DirectorySearcher(root, $"(objectSid={sid})", Properties) { ClientTimeout = TimeSpan.FromSeconds(15) })
                    {
                        result = gcSearcher.FindOne();
                    }

                    break;
                }
            }

            if (result is not null)
            {
                user.DisplayName = Get<string>(result, "displayName");
                user.Email = Get<string>(result, "mail");
                user.DistinguishedName = Get<string>(result, "distinguishedName");

                if (result.Properties["userAccountControl"] is { Count: > 0 } uac && uac[0] is int flags)
                {
                    user.Enabled = (flags & 0x2) == 0;
                }

                if (result.Properties["lastLogonTimestamp"] is { Count: > 0 } ll && ll[0] is long fileTime && fileTime > 0)
                {
                    user.LastLogon = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
                }
            }
        }
        catch (Exception ex)
        {
            user.Error = "LDAP: " + ex.Message;
        }

        return user;
    }

    private static T? Get<T>(SearchResult result, string name)
        where T : class
    {
        var values = result.Properties[name];
        return values is { Count: > 0 } ? values[0] as T : null;
    }

    private static bool DetectDomain()
    {
        try
        {
            System.DirectoryServices.ActiveDirectory.Domain.GetComputerDomain();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
