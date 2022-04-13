using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using ApiService.OneFuzzLib.Orm;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace Microsoft.OneFuzz.Service;

public interface IUpdates
{
    public Task ExecuteUpdate(Update update);
}

public class Updates : IUpdates
{
    private readonly ILogTracer _logger;
    IProxyOperations _proxyOperations;
    INodeOperations _nodeOperations;
    public Updates(ILogTracerFactory loggerFactory, IProxyOperations proxyOperations, INodeOperations nodeOperations)
    {
        _logger = loggerFactory.MakeLogTracer(Guid.NewGuid());
        _proxyOperations = proxyOperations;
        _nodeOperations = nodeOperations;
    }

    public async Task ExecuteUpdate(Update update)
    {
        switch (update.UpdateType)
        {
            // TODO: Task, Job, Repro, Pool
            case UpdateType.Proxy:
                await GenericUpdate<Proxy, IProxyOperations>(update, _proxyOperations);
                break;
            case UpdateType.Node:
                await GenericUpdate<Node, INodeOperations>(update, _nodeOperations);
                break;
            case UpdateType.Scaleset:
                return;
            default:
                throw new NotImplementedException($"unimplemented update type: {update.UpdateType}");
        }
    }

    private async Task GenericUpdate<TEntity, TOperation>(Update update, TOperation operation)
        where TEntity : EntityBase
        where TOperation : IOrm<TEntity>
    {
        if (update.ParititionKey == null || update.RowKey == null)
        {
            throw new ArgumentException($"unsupported update {update}");
        }

        var obj = await operation.QueryAsync(filter: $"RowKey eq '{update.RowKey}' and PartitionKey eq '{update.ParititionKey}'").FirstOrDefaultAsync();
        if (obj == null)
        {
            _logger.Error($"unable to find obj to update {update}");
            return;
        }

        if (update.Method != null)
        {
            var method = obj.GetType().GetMethod(update.Method);
            if (method != null)
            {
                _logger.Info($"performing queued update: {update}");
                method.Invoke(null, null);
                return;
            }
        }
        else
        {
            var state = obj.GetType().GetMember("state");
            if (state == null)
            {
                _logger.Error($"queued update for object without state: {update}");
                return;
            }

            var func = state.GetType().GetMethod("name");
            if (func == null)
            {
                // TODO: We need debug in logger
                _logger.Info($"no function to implement state: {update} - {state}");
                return;
            }
            _logger.Info($"performing queued update for state: {update} - {func.Name}");

            func.Invoke(null, null);
        }
        return;
    }
}
