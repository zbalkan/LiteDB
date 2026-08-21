using System;

namespace LiteDB.AOT
{
    /// <summary>
    /// Enables generated or explicitly registered entity mappers on a BsonMapper.
    /// </summary>
    public static class GeneratedMappersExtensions
    {
        /// <summary>
        /// Use generated or explicitly registered entity mappers.
        /// </summary>
        public static BsonMapper UseGeneratedMappers(
            this BsonMapper mapper,
            IEntityMapperProvider provider,
            bool strict = true)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            mapper.Generated = new GeneratedContractRegistry(provider, strict);
            return mapper;
        }
    }
}