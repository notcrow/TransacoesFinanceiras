namespace BuildingBlocks.Domain.Entities;

public class Account
{
    public Guid Id { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
}
