namespace Homestay.DTO.Auth;

public sealed record LoginRequest
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string MachineName { get; init; } = "";
    public string AppVersion { get; init; } = "";
}
