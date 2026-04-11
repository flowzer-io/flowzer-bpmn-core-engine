namespace WebApiEngine.Shared;

public class HealthStatusDto
{
    public required string Status { get; set; }
    public required DateTime CheckedAtUtc { get; set; }
    public required string Environment { get; set; }
    public required string Storage { get; set; }
}
