using System.Net;

namespace ElasticDesktopManager.Core.Es;

/// <summary>ES 访问异常（携带用户可读消息）。</summary>
public class EsException : Exception
{
    public int? StatusCode { get; }

    public EsException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}