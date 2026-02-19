namespace SettlementWorker.Persistence.Entities;

public class Account
{
    public Guid Id { get; set; }
    public string HolderName { get; set; } = "";
    public decimal Balance { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
}
