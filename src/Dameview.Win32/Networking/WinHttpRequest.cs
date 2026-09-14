using Microsoft.Win32.SafeHandles;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.WinHttp;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32.Networking;

internal sealed unsafe class WinHttpRequest : IDisposable
{
    private const int TimeoutMilliseconds = 10_000;

    private readonly WinHttpHandle _session;
    private readonly WinHttpHandle _connection;
    private readonly WinHttpHandle _request;

    private WinHttpRequest(string host, string method, string path, string userAgent)
    {
        _session = WinHttpHandle.Create(WinHttpOpen(
            userAgent,
            WINHTTP_ACCESS_TYPE.WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
            null,
            null,
            0));

        try
        {
            RequireSuccess(WinHttpSetTimeouts(
                _session.Pointer,
                TimeoutMilliseconds,
                TimeoutMilliseconds,
                TimeoutMilliseconds,
                TimeoutMilliseconds),
                "Could not configure the HTTP request");
            _connection = WinHttpHandle.Create(WinHttpConnect(
                _session.Pointer,
                host,
                INTERNET_DEFAULT_HTTPS_PORT,
                0));
        }
        catch
        {
            _session.Dispose();
            throw;
        }

        try
        {
            PCWSTR acceptTypes = default;
            _request = WinHttpHandle.Create(WinHttpOpenRequest(
                _connection.Pointer,
                method,
                path,
                null!,
                null!,
                in acceptTypes,
                WINHTTP_OPEN_REQUEST_FLAGS.WINHTTP_FLAG_SECURE));
        }
        catch
        {
            _connection.Dispose();
            _session.Dispose();
            throw;
        }
    }

    internal static WinHttpRequest Send(string host, string method, string path, string userAgent)
    {
        var request = new WinHttpRequest(host, method, path, userAgent);
        try
        {
            RequireSuccess(WinHttpSendRequest(
                request._request.Pointer,
                null,
                0,
                [],
                0,
                0),
                "Could not send the HTTP request");
            RequireSuccess(
                WinHttpReceiveResponse(request._request.Pointer, null),
                "Could not receive the HTTP response");
            request.ValidateStatus();
            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    internal string GetFinalUrl()
    {
        uint byteCount = 0;
        _ = WinHttpQueryOption(_request.Pointer, WINHTTP_OPTION_URL, null, &byteCount);
        if (byteCount < sizeof(char))
        {
            throw NativeMethods.CreateLastErrorException("Could not read the final HTTP URL");
        }

        char[] buffer = new char[checked((int)(byteCount / sizeof(char)))];
        fixed (char* bufferPointer = buffer)
        {
            RequireSuccess(
                WinHttpQueryOption(_request.Pointer, WINHTTP_OPTION_URL, bufferPointer, &byteCount),
                "Could not read the final HTTP URL");
            int length = checked((int)(byteCount / sizeof(char)));
            while (length > 0 && bufferPointer[length - 1] == '\0')
            {
                length--;
            }

            return new string(bufferPointer, 0, length);
        }
    }

    internal void CopyResponseTo(Stream output, long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        byte[] buffer = new byte[64 * 1024];
        long totalBytes = 0;
        while (true)
        {
            uint bytesRead = 0;
            RequireSuccess(
                WinHttpReadData(_request.Pointer, buffer, ref bytesRead),
                "Could not read the HTTP response");
            if (bytesRead == 0)
            {
                return;
            }

            totalBytes += bytesRead;
            if (totalBytes > maximumBytes)
            {
                throw new InvalidDataException("The HTTP response is larger than allowed.");
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

    private void ValidateStatus()
    {
        uint status = 0;
        uint size = sizeof(uint);
        uint index = 0;
        RequireSuccess(WinHttpQueryHeaders(
            _request.Pointer,
            WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
            default,
            &status,
            &size,
            &index),
            "Could not read the HTTP response status");

        if (status is < 200 or >= 300)
        {
            throw new InvalidDataException($"The server returned HTTP {status}.");
        }
    }

    private static void RequireSuccess(BOOL succeeded, string operation)
    {
        if (!succeeded)
        {
            throw NativeMethods.CreateLastErrorException(operation);
        }
    }

    private sealed class WinHttpHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private WinHttpHandle()
            : base(ownsHandle: true)
        {
        }

        internal void* Pointer => (void*)DangerousGetHandle();

        internal static WinHttpHandle Create(void* value)
        {
            if (value is null)
            {
                throw NativeMethods.CreateLastErrorException("Could not create a WinHTTP handle");
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
