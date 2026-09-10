using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Bootstrap.Controller;
using Naraka.Features.Bootstrap.Model;
using UnityEngine;
using UnityEngine.Networking;

namespace Naraka.Infrastructure.Config
{
    public sealed class UnityConfigVersionGateway : IConfigVersionGateway
    {
        private const int RequestTimeoutSeconds = 8;
        private const int MaximumResponseCharacters = 4096;
        private readonly string _endpoint;

        public UnityConfigVersionGateway(string baseUrl)
        {
            var normalized = baseUrl?.Trim().TrimEnd('/') ?? string.Empty;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps &&
                 !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            {
                throw new ArgumentException(
                    "Bootstrap URL must use HTTPS, except for loopback development.",
                    nameof(baseUrl));
            }

            _endpoint = normalized + "/bootstrap/config-version";
        }

        public async UniTask<ConfigVersionManifest> GetRequiredVersionAsync(
            CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Get(_endpoint);
            request.timeout = RequestTimeoutSeconds;
            await request.SendWebRequest().WithCancellation(cancellationToken);
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw request.result == UnityWebRequest.Result.ConnectionError
                    ? new TimeoutException("Bootstrap endpoint is unavailable.")
                    : new InvalidOperationException("Bootstrap endpoint rejected the request.");
            }

            var json = request.downloadHandler.text ?? string.Empty;
            if (json.Length == 0 || json.Length > MaximumResponseCharacters)
            {
                throw new InvalidOperationException("Bootstrap response length is invalid.");
            }

            var response = JsonUtility.FromJson<ConfigVersionResponse>(json);
            if (response == null)
            {
                throw new InvalidOperationException("Bootstrap response is invalid.");
            }

            return new ConfigVersionManifest(
                response.configVersion,
                response.minimumClientVersion,
                response.maximumClientVersion,
                response.protocolVersion,
                response.serverCapabilities);
        }

        [Serializable]
        private sealed class ConfigVersionResponse
        {
            public string configVersion;
            public string minimumClientVersion;
            public string maximumClientVersion;
            public string protocolVersion;

            // 可选字段。旧云端 Host 的响应里没有它，JsonUtility 会保持 null，
            // 客户端据此退回 P1.1-A 兼容模式而不是把它当成"服务器没有任何功能"。
            public string[] serverCapabilities;
        }
    }
}
