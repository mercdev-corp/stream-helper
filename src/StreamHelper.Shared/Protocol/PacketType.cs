namespace StreamHelper.Shared.Protocol;

public enum PacketType : byte
{
    Heartbeat = 1,
    StateChange = 2,
    Goodbye = 3
}
