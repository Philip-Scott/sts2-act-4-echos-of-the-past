using System.Text.Json;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace TheArchitect.TheArchitectCode.Multiplayer;

public sealed class LobbyDeckPreviewMessage : ICustomMessage
{
    public LobbyPreviewPacket Packet { get; set; } = new();
    public bool ShouldBroadcast => false;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt((int)Packet.Kind);
        writer.WriteString(Packet.RequestId);
        writer.WriteString(Packet.Epoch);
        writer.WriteInt(Packet.MemberNetIds.Length);
        foreach (var member in Packet.MemberNetIds)
            writer.WriteULong(member);
        writer.WriteString(Packet.Payload);
        writer.WriteString(Packet.Digest);
        writer.WriteInt(Packet.Index);
        writer.WriteInt(Packet.Count);
    }

    public void Deserialize(PacketReader reader)
    {
        var kind = (LobbyPreviewMessageKind)reader.ReadInt();
        var request = reader.ReadString();
        var epoch = reader.ReadString();
        int count = reader.ReadInt();
        if (count is < 1 or > 4)
            throw new JsonException("Invalid Corrupted Party preview membership size.");
        var members = new ulong[count];
        for (var i = 0; i < count; i++)
            members[i] = reader.ReadULong();
        Packet = new LobbyPreviewPacket
        {
            Kind = kind,
            RequestId = request,
            Epoch = epoch,
            MemberNetIds = members,
            Payload = reader.ReadString(),
            Digest = reader.ReadString(),
            Index = reader.ReadInt(),
            Count = reader.ReadInt()
        };
    }

    public void HandleMessage(ulong senderId) =>
        MainFile.Logger.Warn("Ignored a lobby deck-preview message after the run started.");
}
