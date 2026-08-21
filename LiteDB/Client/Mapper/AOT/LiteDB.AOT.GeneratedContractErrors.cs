using System;

namespace LiteDB.AOT
{
    /// <summary>
    /// Factories for errors raised by the generated-contract (AOT) mapper path.
    /// </summary>
    internal static class GeneratedContractErrors
    {
        internal static LiteException GeneratedMapperNotFound(Type type)
        {
            return new LiteException(
                LiteException.ILLEGAL_DESERIALIZATION_TYPE,
                "No generated entity mapper is registered for type '{0}'.", type.FullName);
        }
    }
}