namespace Dameview.Navigation;

/// <summary>
/// Shares one <see cref="FolderSource"/> between everyone showing the same scope, for as long as
/// anyone does. Owner-thread confined.
/// </summary>
internal sealed class FolderSources(
    Func<FolderScope, FolderSource> createSource,
    SynchronizationContext ownerContext)
{
    private readonly Dictionary<FolderScope, Shared> _shared = [];

    /// <summary>
    /// Subscribing to a scope that has already sent updates delivers them first, as one update.
    /// That is posted like the rest, so it never arrives while the subscriber is still opening
    /// the folder.
    /// </summary>
    internal IDisposable Subscribe(FolderScope scope, Action<FolderUpdate> updated)
    {
        if (!_shared.TryGetValue(scope, out Shared? shared))
        {
            shared = new Shared(createSource(scope));
            _shared.Add(scope, shared);
            shared.Source.Start();
        }

        var subscription = new Subscription(this, shared, updated);
        shared.Subscriptions.Add(subscription);
        if (shared.Source.Snapshot is null)
        {
            subscription.Joined = true;
        }
        else
        {
            ownerContext.Post(_ => Join(subscription), null);
        }

        return subscription;
    }

    private static void Join(Subscription subscription)
    {
        if (!subscription.Released)
        {
            subscription.Joined = true;
            subscription.Updated(subscription.Shared.Source.Snapshot!);
        }
    }

    private void Release(Subscription subscription)
    {
        if (subscription.Released)
        {
            return;
        }

        subscription.Released = true;
        Shared shared = subscription.Shared;
        shared.Subscriptions.Remove(subscription);
        if (shared.Subscriptions.Count == 0)
        {
            _shared.Remove(shared.Source.Scope);
            shared.Source.Dispose();
        }
    }

    private sealed class Shared
    {
        internal Shared(FolderSource source)
        {
            Source = source;
            source.Updated += update =>
            {
                foreach (Subscription subscription in Subscriptions.ToArray())
                {
                    if (subscription.Joined && !subscription.Released)
                    {
                        subscription.Updated(update);
                    }
                }
            };
        }

        internal FolderSource Source { get; }
        internal List<Subscription> Subscriptions { get; } = [];
    }

    private sealed class Subscription(FolderSources owner, Shared shared, Action<FolderUpdate> updated)
        : IDisposable
    {
        internal Shared Shared { get; } = shared;
        internal Action<FolderUpdate> Updated { get; } = updated;
        internal bool Joined { get; set; }
        internal bool Released { get; set; }

        public void Dispose() => owner.Release(this);
    }
}
