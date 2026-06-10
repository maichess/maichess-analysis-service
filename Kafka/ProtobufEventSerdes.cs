using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Google.Protobuf;

namespace MaichessAnalysisService.Kafka;

// Confluent Protobuf serde factory for the maichess.events.v1 analysis messages
// (AnalysisCommand produced to analysis.commands.v1, AnalysisEvent consumed from
// analysis.events.v1). These events are Protobuf-native from the start (Kafka
// task 07), so there is no Avro path here — unlike the live topics that are still
// mid-migration. Mirrors the match-manager helper; the only Kafka-specific
// dependency is Confluent.SchemaRegistry.Serdes.Protobuf, the generated types
// ship in Maichess.PlatformProtos alongside the gRPC stubs.
[ExcludeFromCodeCoverage]
internal static class ProtobufEventSerdes
{
    public static IAsyncSerializer<T> Serializer<T>(ISchemaRegistryClient registry)
        where T : class, IMessage<T>, new()
        => new ProtobufSerializer<T>(registry);

    public static IDeserializer<T> Deserializer<T>()
        where T : class, IMessage<T>, new()
        => new ProtobufDeserializer<T>().AsSyncOverAsync();
}
