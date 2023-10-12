using HotChocolate.Data.Filters;
using HotChocolate.Data.Filters.Expressions;

namespace LexBoxApi.GraphQL.CustomFilters;

public static class CustomFilterOperations
{
    public const int IContains = 1025;

    public static IFilterConventionDescriptor AddDeterministicInvariantContainsFilter(this IFilterConventionDescriptor descriptor)
    {
        descriptor.Operation(IContains)
            .Name("icontains");
        descriptor.Configure<StringOperationFilterInputType>(
            x => x.Operation(IContains).Type<StringType>());
        descriptor.AddProviderExtension(new QueryableFilterProviderExtension(y => y
                    .AddFieldHandler<QueryableStringDeterministicInvariantContainsHandler>()));
        return descriptor;
    }
}
