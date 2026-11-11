using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.WinHttp;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32.Networking;

internal sealed record HttpError(string Message);

internal readonly union HttpResult(WinHttpRequest, HttpError);

internal readonly union HttpUrlResult(string, HttpError);

/// <summary>How many bytes were copied, or why the copy failed.</summary>
internal readonly union HttpCopyResult(long, HttpError);

internal sealed unsafe class WinHttpRequest : IDisposable
{
    private const int TimeoutMilliseconds = 10_000;

    private readonly WinHttpHandle _session;
    private readonly WinHttpHandle _connection;
    private readonly WinHttpHandle _request;

    private WinHttpRequest(WinHttpHandle session, WinHttpHandle connection, WinHttpHandle request)
    {
        _session = session;
        _connection = connection;
        _request = request;
    }

    /// <returns>The request once a successful (2xx) response has arrived, or why none did.</returns>
    internal static HttpResult Send(string host, string method, string path, string userAgent)
    {
        // Each handle is released here unless it was handed to the finished request.
        WinHttpHandle? session = null;
        WinHttpHandle? connection = null;
        WinHttpHandle? request = null;
        try
        {
            session = WinHttpHandle.Create(WinHttpOpen(
                userAgent,
                WINHTTP_ACCESS_TYPE.WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                null,
                null,
                0));
            if (session is null)
            {
                return LastError("Could not open an HTTP session");
            }

            if (!WinHttpSetTimeouts(
                session.Pointer,
                TimeoutMilliseconds,
                TimeoutMilliseconds,
                TimeoutMilliseconds,
                TimeoutMilliseconds))
            {
                return LastError("Could not configure the HTTP request");
            }

            connection = WinHttpHandle.Create(WinHttpConnect(session.Pointer, host, INTERNET_DEFAULT_HTTPS_PORT, 0));
            if (connection is null)
            {
                return LastError("Could not connect to the server");
            }

            PCWSTR acceptTypes = default;
            request = WinHttpHandle.Create(WinHttpOpenRequest(
                connection.Pointer,
                method,
                path,
                null!,
                null!,
                in acceptTypes,
                WINHTTP_OPEN_REQUEST_FLAGS.WINHTTP_FLAG_SECURE));
            if (request is null)
            {
                return LastError("Could not create the HTTP request");
            }

            if (!WinHttpSendRequest(request.Pointer, null, 0, [], 0, 0))
            {
                return LastError("Could not send the HTTP request");
            }

            if (!WinHttpReceiveResponse(request.Pointer, null))
            {
                return LastError("Could not receive the HTTP response");
            }

            uint status = 0;
            uint size = sizeof(uint);
            uint index = 0;
            if (!WinHttpQueryHeaders(
                request.Pointer,
                WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                default,
                &status,
                &size,
                &index))
            {
                return LastError("Could not read the HTTP response status");
            }

            if (status is < 200 or >= 300)
            {
                return new HttpError($"The server returned HTTP {status}.");
            }

            var sent = new WinHttpRequest(session, connection, request);
            (session, connection, request) = (null, null, null);
            return sent;
        }
        finally
        {
            request?.Dispose();
            connection?.Dispose();
            session?.Dispose();
        }
    }

    internal HttpUrlResult GetFinalUrl()
    {
        uint byteCount = 0;
        _ = WinHttpQueryOption(_request.Pointer, WINHTTP_OPTION_URL, null, &byteCount);
        if (byteCount < sizeof(char))
        {
            return LastError("Could not read the final HTTP URL");
        }

        char[] buffer = new char[checked((int)(byteCount / sizeof(char)))];
        fixed (char* bufferPointer = buffer)
        {
            if (!WinHttpQueryOption(_request.Pointer, WINHTTP_OPTION_URL, bufferPointer, &byteCount))
            {
                return LastError("Could not read the final HTTP URL");
            }

            int length = checked((int)(byteCount / sizeof(char)));
            while (length > 0 && bufferPointer[length - 1] == '\0')
            {
                length--;
            }

            return new string(bufferPointer, 0, length);
        }
    }

    internal HttpCopyResult CopyResponseTo(Stream output, long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        byte[] buffer = new byte[64 * 1024];
        long totalBytes = 0;
        while (true)
        {
            uint bytesRead = 0;
            if (!WinHttpReadData(_request.Pointer, buffer, ref bytesRead))
            {
                return LastError("Could not read the HTTP response");
            }

            if (bytesRead == 0)
            {
                return totalBytes;
            }

            totalBytes += bytesRead;
            if (totalBytes > maximumBytes)
            {
                return new HttpError("The HTTP response is larger than allowed.");
            }

            output.Write(buffer, 0, checked((int)bytesRead));
        }
    }

    public void Dispose()
    {
        _request.Dispose();
        _connection.Dispose();
        _session.Dispose();
    }

    // Must run right after the failed call, before anything else can overwrite the error.
    private static HttpError LastError(string operation)
    {
        return new HttpError($"{operation}: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
    }

    private sealed class WinHttpHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private WinHttpHandle()
            : base(ownsHandle: true)
        {
        }

        internal void* Pointer => (void*)DangerousGetHandle();

        /// <returns>Null when the call that made the handle failed.</returns>
        internal static WinHttpHandle? Create(void* value)
        {
            if (value is null)
            {
                return null;
            }

            var handle = new WinHttpHandle();
            handle.SetHandle((nint)value);
            return handle;
        }

        protected override bool ReleaseHandle()
        {
            return WinHttpCloseHandle((void*)handle);
        }
    }
}
