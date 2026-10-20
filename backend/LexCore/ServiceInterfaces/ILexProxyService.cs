using LexCore.Auth;
using LexCore.Entities;
using LexSyncReverseProxy;

namespace LexCore.ServiceInterfaces;

public interface ILexProxyService
{
    Task<LexAuthUser?> Login(LoginRequest loginRequest);
    Task QueueProjectMetadataUpdate(string projectCode);
    ValueTask<Guid?> LookupProjectId(string projectCode);
    ValueTask<RetentionPolicy> LookupProjectRetentionPolicy(Guid projectId);
    RequestInfo GetDestinationPrefix(HgType type);
}

public record RequestInfo(string DestinationPrefix);
