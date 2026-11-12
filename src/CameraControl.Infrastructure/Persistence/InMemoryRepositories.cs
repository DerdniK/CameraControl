using System.Collections.Concurrent;
using CameraControl.Domain.Entities;
using CameraControl.Domain.Ports;

namespace CameraControl.Infrastructure.Persistence;

public sealed class InMemoryChildNodeRepository : IChildNodeRepository
{
    private readonly ConcurrentDictionary<Guid, ChildServerNode> _nodes = new();

    public Task<IReadOnlyList<ChildServerNode>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = _nodes.Values
            .OrderBy(n => n.CreatedAtUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<ChildServerNode>>(list);
    }

    public Task<ChildServerNode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _nodes.TryGetValue(id, out var node);
        return Task.FromResult(node);
    }

    public Task<ChildServerNode?> GetByIpAsync(string assignedIp, CancellationToken cancellationToken = default)
    {
        var normalized = assignedIp.Trim();
        var match = _nodes.Values.FirstOrDefault(n =>
            string.Equals(n.AssignedIp, normalized, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task AddAsync(ChildServerNode node, CancellationToken cancellationToken = default)
    {
        _nodes[node.Id] = node;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ChildServerNode node, CancellationToken cancellationToken = default)
    {
        _nodes[node.Id] = node;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_nodes.TryRemove(id, out _));
    }
}

public sealed class InMemoryCaptureHistoryRepository : ICaptureHistoryRepository
{
    private readonly ConcurrentQueue<CameraCaptureResult> _captures = new();
    private const int MaxStoredCaptures = 500;

    public Task AddAsync(CameraCaptureResult capture, CancellationToken cancellationToken = default)
    {
        _captures.Enqueue(capture);
        while (_captures.Count > MaxStoredCaptures && _captures.TryDequeue(out _))
        {
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CameraCaptureResult>> GetRecentAsync(
        int limit = 50,
        Guid? childNodeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _captures.Reverse();
        if (childNodeId.HasValue)
        {
            query = query.Where(c => c.ChildNodeId == childNodeId.Value);
        }

        var result = query.Take(Math.Clamp(limit, 1, MaxStoredCaptures)).ToList();
        return Task.FromResult<IReadOnlyList<CameraCaptureResult>>(result);
    }

    public Task<CameraCaptureResult?> GetByIdAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        var match = _captures.FirstOrDefault(c => c.CaptureId == captureId);
        return Task.FromResult(match);
    }
}
