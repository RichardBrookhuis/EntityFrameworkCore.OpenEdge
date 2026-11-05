using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.OpenEdge.Query.ExpressionVisitors.Internal;

/// <summary>
///     <para>
///         A class that processes a SQL tree based on nullability of nodes to apply null semantics in use and
///         optimize it based on parameter values.
///     </para>
///     <para>
///         This type is typically used by database providers (and other extensions). It is generally
///         not used in application code.
///     </para>
/// </summary>
public class OpenEdgeSqlNullabilityProcessor : SqlNullabilityProcessor // Use this for tinkering/debugging: OriginalSqlNullabilityProcessor
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>
    ///     Creates a new instance of the <see cref="SqlNullabilityProcessor" /> class.
    /// </summary>
    /// <param name="dependencies">Parameter object containing dependencies for this class.</param>
    /// <param name="parameters">Parameter object containing parameters for this class.</param>
    public OpenEdgeSqlNullabilityProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters) : base(dependencies, parameters)
    {
        _sqlExpressionFactory = dependencies.SqlExpressionFactory;
    }


    /// <summary>
    ///     Visits a <see cref="SqlBinaryExpression" /> and computes its nullability.
    /// </summary>
    /// <param name="sqlBinaryExpression">A sql binary expression to visit.</param>
    /// <param name="allowOptimizedExpansion">A bool value indicating if optimized expansion which considers null value as false value is allowed.</param>
    /// <param name="nullable">A bool value indicating whether the sql expression is nullable.</param>
    /// <returns>An optimized sql expression.</returns>
    protected override SqlExpression VisitSqlBinary(
        SqlBinaryExpression sqlBinaryExpression,
        bool allowOptimizedExpansion,
        out bool nullable)
    {


        // The if statement below is copied from base.VisitSqlBinary() and modified
        // to prevent that a comparison with a boolean constant is optimized (removed).
        // Progres OpenEdge demandw explicit comparison with boolean constants, like: "WHERE table.booleanfield = true"
        // and does not understand an optimized "WHERE table.booleanfield"
        // Original comment: 

        // Most optimizations are done in OptimizeComparison below, but this one
        // benefits from being done early.
        // Consider query: (x.NullableString == "Foo") == true
        // We recursively visit Left and Right, but when processing the left
        // side, allowOptimizedExpansion would be set to false (we only allow it
        // to trickle down to child nodes for AndAlso & OrElse operations), so
        // the comparison would get unnecessarily expanded. In order to avoid
        // this, we would need to modify the allowOptimizedExpansion calculation
        // to capture this scenario and then flow allowOptimizedExpansion to
        // OptimizeComparison. Instead, we just do the optimization right away
        // and the resulting code is clearer.
        if (allowOptimizedExpansion && sqlBinaryExpression.OperatorType == ExpressionType.Equal)
        {
            if (IsTrue(sqlBinaryExpression.Left) && sqlBinaryExpression.Left.TypeMapping!.Converter == null)
            {
                // if the right side itself is a SqlBinaryExpression, then the original code should do
                if (sqlBinaryExpression.Right is SqlBinaryExpression)
                    return Visit(sqlBinaryExpression.Right, allowOptimizedExpansion, out nullable);
                else 
                    return sqlBinaryExpression.Update(sqlBinaryExpression.Left, Visit(sqlBinaryExpression.Right, allowOptimizedExpansion, out nullable));
            }

            if (IsTrue(sqlBinaryExpression.Right) && sqlBinaryExpression.Right.TypeMapping!.Converter == null)
            {
                // if the left side itself is a SqlBinaryExpression, then the original code should do
                if (sqlBinaryExpression.Left is SqlBinaryExpression) 
                    return Visit(sqlBinaryExpression.Left, allowOptimizedExpansion, out nullable);
                else 
                    return sqlBinaryExpression.Update(Visit(sqlBinaryExpression.Left, allowOptimizedExpansion, out nullable), sqlBinaryExpression.Right);
            }
        }
        
        return base.VisitSqlBinary(sqlBinaryExpression, allowOptimizedExpansion, out nullable); 
    }





    /// <summary>
    ///     Visits a <see cref="SqlUnaryExpression" /> and computes its nullability.
    /// </summary>
    /// <param name="sqlUnaryExpression">A sql unary expression to visit.</param>
    /// <param name="allowOptimizedExpansion">A bool value indicating if optimized expansion which considers null value as false value is allowed.</param>
    /// <param name="nullable">A bool value indicating whether the sql expression is nullable.</param>
    /// <returns>An optimized sql expression.</returns>
    protected override SqlExpression VisitSqlUnary(
        SqlUnaryExpression sqlUnaryExpression,
        bool allowOptimizedExpansion,
        out bool nullable)
    {
        return base.VisitSqlUnary(sqlUnaryExpression, allowOptimizedExpansion, out nullable);
    }

    #nullable enable
    private bool IsTrue(SqlExpression? expression)
        => expression is SqlConstantExpression { Value: true };


}

