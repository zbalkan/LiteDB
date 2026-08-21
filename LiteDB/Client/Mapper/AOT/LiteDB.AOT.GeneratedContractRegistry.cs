using System;

namespace LiteDB.AOT
{
    /// <summary>
    /// Owns the generated contract providers installed on a BsonMapper.
    /// </summary>
    internal sealed class GeneratedContractRegistry
    {
        public GeneratedContractRegistry(IEntityMapperProvider provider, bool strict)
        {
            this.EntityMapperProvider = provider;
            this.ValueProvider = provider as IGeneratedValueProvider;
            this.CollectionProvider = provider as IGeneratedCollectionProvider;
            this.Strict = strict;
        }

        public IEntityMapperProvider EntityMapperProvider { get; }

        public IGeneratedValueProvider ValueProvider { get; }

        public IGeneratedCollectionProvider CollectionProvider { get; }

        public bool Strict { get; }

        public bool TryGetEntityMapper(Type type, out EntityMapper mapper)
        {
            if (this.EntityMapperProvider != null && this.EntityMapperProvider.TryGet(type, out mapper))
            {
                return true;
            }

            mapper = null;
            return false;
        }

        public bool TryGetValueContract(
            Type type,
            out Func<BsonMapper, object, BsonValue> serialize,
            out Func<BsonMapper, BsonValue, object> deserialize)
        {
            if (this.ValueProvider != null && this.ValueProvider.TryGet(type, out serialize, out deserialize))
            {
                return true;
            }

            serialize = null;
            deserialize = null;
            return false;
        }

        public bool TryGetCollectionContract(
            Type type,
            out Func<BsonMapper, object, BsonArray> serialize,
            out Func<BsonMapper, BsonArray, object> deserialize)
        {
            if (this.CollectionProvider != null && this.CollectionProvider.TryGet(type, out serialize, out deserialize))
            {
                return true;
            }

            serialize = null;
            deserialize = null;
            return false;
        }
    }
}