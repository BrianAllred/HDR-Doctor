namespace HdrDoctor.Core.Sources;

/// <summary>
/// An FTP server refused the username and password it was given.
/// </summary>
/// <remarks>
/// The one connection failure a user can fix by typing, so it is worth telling apart
/// from an unreachable host. FluentFTP's own exception types stop at
/// <see cref="FtpSource.ConnectAsync"/> and this crosses the boundary instead, so
/// deciding to prompt does not mean knowing what the transport is.
/// </remarks>
public sealed class FtpLoginRefusedException(string message, Exception inner)
    : Exception(message, inner);
