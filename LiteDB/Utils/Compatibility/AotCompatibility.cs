namespace LiteDB
{
    internal static class AotCompatibility
    {
        public const string ModelMappingRequiresSourceGen =
            "Runtime entity mapping is incompatible with NativeAOT. Annotate entities with [LiteEntity] and use 'BsonMapper.UseGeneratedMappers()'.";
        public const string RuntimeTypeConstruction =
            "Constructing arrays or closed generic types at runtime is not available under NativeAOT.";
        public const string CollectionNameRequiresSourceGen =
            "Resolving collection names via runtime interface inspection is incompatible with trimming. Pass an explicit collection name string in NativeAOT.";
        public const string PersistedTypeResolution =
            "Resolving persisted type names requires runtime type lookup and cannot guarantee that the resolved type is preserved.";
        internal const string InternalReflectionHelperJustification =
            "Guarded by [RequiresDynamicCode]/[RequiresUnreferencedCode] on public entry points and completely bypassed when source generators are used.";
        internal const string ExpressionParserInterpreterJustification =
            "Expression trees are evaluated via LiteDB's interpreter when dynamic compilation is unavailable under NativeAOT.";
    }
}
