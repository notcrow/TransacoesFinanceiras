namespace SettlementWorker.Persistence.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public int Type { get; set; }     // 1=Debit, 2=Credit (não vamos depender disso)
    public int Status { get; set; }   // 4=Settled
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
