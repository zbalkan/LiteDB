using System;

namespace LiteDB.AOT
{
    /// <summary>
    /// Supplies pre-built entity mappers for generated or explicitly registered types.
    /// </summary>
    public interface IEntityMapperProvider
    {
        bool TryGet(Type type, out EntityMapper mapper);
    }

    /// <summary>
    /// Supplies generated value contracts, such as closed hierarchy dispatchers.
    /// </summary>
    public interface IGeneratedValueProvider
    {
        bool TryGet(
            Type type,
            out Func<BsonMapper, object, BsonValue> serialize,
            out Func<BsonMapper, BsonValue, object> deserialize);
    }

    /// <summary>
    /// Supplies generated adapters for supported collection shapes.
    /// </summary>
    public interface IGeneratedCollectionProvider
    {
        bool TryGet(
            Type type,
            out Func<BsonMapper, object, BsonArray> serialize,
            out Func<BsonMapper, BsonArray, object> deserialize);
    }
}