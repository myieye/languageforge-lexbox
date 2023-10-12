using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Filters.Expressions;
using HotChocolate.Language;
using Microsoft.EntityFrameworkCore;

namespace LexBoxApi.GraphQL.CustomFilters;

/// <summary>
/// We use nondeterministic, case insensitive collations for some Postgres columns.
/// Postgres doesn't support substring comparisons on nondeterministic collations, so we offer a
/// case insensitive filter that explicitly uses a deterministic collation (und-x-icu) instead.
/// </summary>
public class QueryableStringDeterministicInvariantContainsHandler : QueryableStringOperationHandler
{
    private static readonly MethodInfo _ilike = typeof(NpgsqlDbFunctionsExtensions).GetMethod("ILike",
            new[] { typeof(DbFunctions), typeof(string), typeof(string) });
    private static readonly MethodInfo _collate = typeof(RelationalDbFunctionsExtensions).GetMethod("Collate")
        .MakeGenericMethod(typeof(string));
    private static readonly ConstantExpression _efFunctions = Expression.Constant(EF.Functions);

    protected override int Operation => CustomFilterOperations.IContains;

    public QueryableStringDeterministicInvariantContainsHandler(InputParser inputParser)
        : base(inputParser)
    {
    }

    public override Expression HandleOperation(
        QueryableFilterContext context,
        IFilterOperationField field,
        IValueNode value,
        object? parsedValue)
    {
        if (parsedValue is string)
        {
            var pattern = $"%{parsedValue}%";
            var property = context.GetInstance();

            var collatedValueExpression = Expression.Call(
                null,
                _collate,
                _efFunctions,
                property,
                // we have to explicitly use a deterministic collation, because Postgres doesn't support LIKE for non-deterministic collations,
                // which we use for some columns
                Expression.Constant("und-x-icu")
            );

            return Expression.AndAlso(
                Expression.NotEqual(property, Expression.Constant(null, typeof(object))),
                Expression.Call(
                    null,
                    _ilike,
                    _efFunctions,
                    collatedValueExpression,
                    Expression.Constant(pattern)
                )
            );
        }
        throw new InvalidOperationException();
    }
}
