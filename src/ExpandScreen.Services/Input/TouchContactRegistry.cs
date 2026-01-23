namespace ExpandScreen.Services.Input
{
    public sealed class TouchContactRegistry
    {
        private readonly object _lock = new();
        private readonly Dictionary<int, int> _androidPointerIdToSlot = new();
        private readonly SortedSet<int> _freeSlots;
        private readonly int _maxContacts;

        public TouchContactRegistry(int maxContacts = 10)
        {
            if (maxContacts <= 0) throw new ArgumentOutOfRangeException(nameof(maxContacts));
            _maxContacts = maxContacts;
            _freeSlots = new SortedSet<int>(Enumerable.Range(1, maxContacts));
        }

        public int MaxContacts => _maxContacts;

        public int? GetSlot(int pointerId)
        {
            lock (_lock)
            {
                return _androidPointerIdToSlot.TryGetValue(pointerId, out var slot) ? slot : null;
            }
        }

        public int? AllocateSlot(int pointerId)
        {
            lock (_lock)
            {
                if (_androidPointerIdToSlot.TryGetValue(pointerId, out var existing))
                {
                    return existing;
                }

                if (_freeSlots.Count == 0)
                {
                    return null;
                }

                int slot = _freeSlots.Min;
                _freeSlots.Remove(slot);
                _androidPointerIdToSlot[pointerId] = slot;
                return slot;
            }
        }

        public bool ReleaseSlot(int pointerId)
        {
            lock (_lock)
            {
                if (!_androidPointerIdToSlot.TryGetValue(pointerId, out var slot))
                {
                    return false;
                }

                _androidPointerIdToSlot.Remove(pointerId);
                _freeSlots.Add(slot);
                return true;
            }
        }

        public int? GetPrimarySlot()
        {
            lock (_lock)
            {
                if (_androidPointerIdToSlot.Count == 0)
                {
                    return null;
                }

                return _androidPointerIdToSlot.Values.Min();
            }
        }
    }
}

