using System;
using LiteDB.AOT;

namespace LiteDB
{
    public partial class BsonMapper
    {
        /// <summary>
        /// Optional generated contract registry used before runtime discovery.
        /// </summary>
        internal GeneratedContractRegistry Generated { get; set; }

        /// <summary>
        /// If true, an entity absent from the registry fails instead of using reflection discovery.
        /// </summary>
        internal bool IsStrictGeneratedMode => this.Generated?.Strict == true;

        internal bool TryGetGeneratedEntityMapper(Type type, out EntityMapper mapper)
        {
            var registry = this.Generated;

            if (registry != null && registry.TryGetEntityMapper(type, out mapper))
            {
                return true;
            }

            mapper = null;
            return false;
        }

        internal bool TryGetGeneratedValueContract(
            Type type,
            out Func<BsonMapper, object, BsonValue> serialize,
            out Func<BsonMapper, BsonValue, object> deserialize)
        {
            var registry = this.Generated;

            if (registry != null && registry.TryGetValueContract(type, out serialize, out deserialize))
            {
                return true;
            }

            serialize = null;
            deserialize = null;
            return false;
        }

        internal bool TryGetGeneratedCollectionContract(
            Type type,
            out Func<BsonMapper, object, BsonArray> serialize,
            out Func<BsonMapper, BsonArray, object> deserialize)
        {
            var registry = this.Generated;

            if (registry != null && registry.TryGetCollectionContract(type, out serialize, out deserialize))
            {
                return true;
            }

            serialize = null;
            deserialize = null;
            return false;
        }
    }
}