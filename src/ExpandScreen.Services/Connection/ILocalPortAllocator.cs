namespace ExpandScreen.Services.Connection
{
    public interface ILocalPortAllocator
    {
        int AllocateEphemeralPort();
    }
}

