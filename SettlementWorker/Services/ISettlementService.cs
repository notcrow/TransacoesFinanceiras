using System.Threading;
using System.Threading.Tasks;
using SettlementWorker.Messaging.Events;

namespace SettlementWorker.Services
{
    public interface ISettlementService
    {
        Task ProcessAsync(TransactionAuthorizedEvent evt, string settledTopic,CancellationToken ct);
    }
}