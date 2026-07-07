using EntityFrameworkCore.OpenEdge.Extensions;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks.Dataflow;

namespace EntityFrameworkCore.OpenEdge.Query.Sql.Internal
{
    public class OpenEdgeSqlGenerator : QuerySqlGenerator
    {
        private bool _existsConditional;
        private readonly IRelationalTypeMappingSource _typeMappingSource;
        private string _lastSeenTableName;
        private string _lastSeenTableSchema;

        public OpenEdgeSqlGenerator(
            QuerySqlGeneratorDependencies dependencies,
            IRelationalTypeMappingSource typeMappingSource
            ) : base(dependencies)
        {
            _typeMappingSource = typeMappingSource;
            _lastSeenTableName = "";
            _lastSeenTableSchema = "";
        }

        /// <summary>
        ///     Generates SQL for a pseudo FROM clause. This is required by some providers when a query has no actual FROM clause.
        /// </summary>
        protected override void GeneratePseudoFromClause()
        {
            // TODO: Check to see where this comes into play

            // OpenEdge requires that SELECT statements always include a table,
            // so we SELECT from the _File metaschema table that always exists,
            // selecting a single row that we know will always exist; the metaschema
            // record for the _File metaschema table itself.

            //Sql.Append(@" FROM pub.""_File"" f WHERE f.""_File-Name"" = '_File'");
        }

        protected override Expression VisitRowNumber(RowNumberExpression rowNumberExpression)
        {
            // This method emulates the following commented code
            /* 
                Sql.Append("ROW_NUMBER() OVER(");
                if (rowNumberExpression.Partitions.Any())
                {
                    Sql.Append("PARTITION BY ");
                    GenerateList(rowNumberExpression.Partitions, e => Visit(e));
                    Sql.Append(" ");
                }

                Sql.Append("ORDER BY ");
                GenerateList(rowNumberExpression.Orderings, e => Visit(e));
                Sql.Append(")");
            */

            string currentTableAlias = null;
            string rowNumberTableAlias = null;
            List<string> partitionByFields = new List<string>();
           
            for (var i = 0; i < rowNumberExpression.Orderings.Count; i++)
            {
                OrderingExpression o = rowNumberExpression.Orderings[i];
                if (o.Expression is ColumnExpression)
                {
                    ColumnExpression c = (ColumnExpression)o.Expression;
                    currentTableAlias = c.TableAlias;

                }
            }
            if ( currentTableAlias == null)
                throw new Exception("VisitRowNumber: Unable to generate ROW_NUMBER emulation query. Cannot determine current table alias");
            if (_lastSeenTableSchema == null || _lastSeenTableName == null) 
                throw new Exception("VisitRowNumber: Unable to generate ROW_NUMBER emulation query. Cannot determine current table and schema");

            Console.WriteLine(currentTableAlias);
            rowNumberTableAlias = currentTableAlias + "rownumber";
            Sql.Append("(SELECT Count(*) ");
            Sql.Append(string.Format("FROM \"{0}\".\"{1}\" AS \"{2}\" ", _lastSeenTableSchema, _lastSeenTableName, rowNumberTableAlias));
            Sql.Append("WHERE ");

            // Fields from PARTITION BY
            for (var i = 0; i < rowNumberExpression.Partitions.Count; i++)
            {
                ColumnExpression c = rowNumberExpression.Partitions[i] as ColumnExpression;
                if (c != null)
                {
                    if (i > 0)
                        Sql.Append(" AND ");
                    Sql.Append(string.Format("\"{0}\".\"{1}\" = \"{2}\".\"{3}\"", rowNumberTableAlias, c.Name, currentTableAlias, c.Name));
                    partitionByFields.Add(c.Name);
                }
            }

            // Fields from ORDER BY but excluding those from PARTITION BY
            for (var i = 0; i < rowNumberExpression.Orderings.Count; i++)
            {
                OrderingExpression o = rowNumberExpression.Orderings[i];
                if (o.Expression is ColumnExpression)
                {
                    ColumnExpression c = (ColumnExpression)o.Expression;
                    if (!partitionByFields.Contains(c.Name)) 
                    {
                        // Add "AND" if the WHERE clause already contains something
                        if ((i == 0 && partitionByFields.Count > 0) || i > 0)
                            Sql.Append(" AND ");
                        Sql.Append(string.Format("\"{0}\".\"{1}\" <= \"{2}\".\"{3}\"", rowNumberTableAlias, c.Name, currentTableAlias, c.Name));
                    }
                }
            }
            Sql.Append(" ) ");


            return rowNumberExpression;
        }

        protected override Expression VisitParameter(ParameterExpression parameterExpression)
        {
            var parameterName = Dependencies.SqlGenerationHelper.GenerateParameterName(parameterExpression.Name);

            // Register the parameter for later binding
            if (Sql.Parameters
                .All(p => p.InvariantName != parameterExpression.Name))
            {
                var typeMapping
                    = _typeMappingSource.GetMapping(parameterExpression.Type);

                /*
                 * What this essentially means is that a standard SQL query like this:
                 *   WHERE Name = @p0 AND Age = @p1
                 *
                 * Needs to be converted to this (for OpenEdge): 
                 *   WHERE Name = ? AND Age = ?
                 *
                 * The parameters are still tracked internally, but the SQL uses positional placeholders.
                 */
                Sql.AddParameter(
                    parameterExpression.Name,
                    parameterName,
                    typeMapping,
                    parameterExpression.Type.IsNullableType());
            }

            // Named parameters not supported in the command text
            // Need to use '?' instead
            Sql.Append("?"); // This appears to be OpenEdge specific!

            return parameterExpression;
        }

        protected override Expression VisitConditional(ConditionalExpression conditionalExpression)
        {
            var visitConditional = base.VisitConditional(conditionalExpression);

            // OpenEdge requires that SELECT statements always include a table,
            // so we SELECT from the _File metaschema table that always exists,
            // selecting a single row that we know will always exist; the metaschema
            // record for the _File metaschema table itself.
            if (_existsConditional)
                Sql.Append(@" FROM pub.""_File"" f WHERE f.""_File-Name"" = '_File'");

            _existsConditional = false;

            return visitConditional;
        }

        // TODO: Double check that this is still needed and create this functionality in an appropriate location
        // protected override Expression VisitExists(ExistsExpression existsExpression)
        // {
        //     // Your OpenEdge-specific EXISTS logic here
        //     // OpenEdge does not support WHEN EXISTS, only WHERE EXISTS
        //     // We need to SELECT 1 using WHERE EXISTS, then compare
        //     // the result to 1 to satisfy the conditional.
        //
        //     // OpenEdge requires that SELECT statements always include a table,
        //     // so we SELECT from the _File metaschema table that always exists,
        //     // selecting a single row that we know will always exist; the metaschema
        //     // record for the _File metaschema table itself.
        //     Sql.AppendLine(@"(SELECT 1 FROM pub.""_File"" f WHERE f.""_File-Name"" = '_File' AND EXISTS (");
        //
        //     using (Sql.Indent())
        //     {
        //         Visit(existsExpression.Subquery);
        //     }
        //
        //     Sql.Append(")) = 1");
        //
        //     _existsConditional = true;
        //
        //     return existsExpression;
        // }

        /// <summary>
        ///     Generates SQL for an EXISTS expression.
        /// </summary>
        /// <param name="existsExpression">The <see cref="ExistsExpression" /> for which to generate SQL.</param>
        /// <param name="negated">Whether the given <paramref name="existsExpression" /> is negated.</param>
        protected override void GenerateExists(ExistsExpression existsExpression, bool negated)
        {
            // Your OpenEdge-specific EXISTS logic here
            // OpenEdge does not support WHEN EXISTS, only WHERE EXISTS
            // We need to SELECT 1 using WHERE EXISTS, then compare
            // the result to 1 to satisfy the conditional.

            // OpenEdge requires that SELECT statements always include a table,
            // so we SELECT from the _File metaschema table that always exists,
            // selecting a single row that we know will always exist; the metaschema
            // record for the _File metaschema table itself.

            // To prevent modifying EXISTS expressions in the WHERE clause, only do this for SELECT EXISTS expressions
            // Note: "SELECT CAST(CASE COUNT(*) WHEN 0 THEN 0 ELSE 1 END AS BIT)" FROM table WHERE expression" produces the same result as "SELECT EXISTS"
            if (Sql.ToString().Trim().ToUpper() == "SELECT")
            {
                if (negated)
                    Sql.AppendLine(@"CAST(CASE COUNT(*) WHEN 0 THEN 1 ELSE 0 END AS BIT) FROM pub.""_File"" f WHERE f.""_File-Name"" = '_File' AND EXISTS (");
                else
                    Sql.AppendLine(@"CAST(CASE COUNT(*) WHEN 0 THEN 0 ELSE 1 END AS BIT) FROM pub.""_File"" f WHERE f.""_File-Name"" = '_File' AND EXISTS (");

                using (Sql.Indent())
                {
                    Visit(existsExpression.Subquery);
                }

                Sql.Append(")");
            }
            else
            {
                base.GenerateExists(existsExpression, negated);
            }
        }

        protected override void GenerateTop(SelectExpression selectExpression)
        {
            // OpenEdge: TOP clause cannot be combined with OFFSET/FETCH clauses
            // Only use TOP if there's no limit/offset that will be handled by GenerateLimitOffset
            // TOP is only used when there's a limit but no offset, and we're not using OFFSET/FETCH
            
            // Don't generate TOP - let GenerateLimitOffset handle all limit/offset cases
            // This avoids the conflict between TOP and FETCH clauses
        }

        protected override void GenerateLimitOffset(SelectExpression selectExpression)
        {
            // https://docs.progress.com/bundle/openedge-sql-reference/page/OFFSET-and-FETCH-clauses.html
            if (selectExpression.Offset != null || selectExpression.Limit != null)
            {
                if (selectExpression.Offset != null)
                {
                    Sql.AppendLine()
                        .Append("OFFSET ");

                    // OpenEdge requires literal values in OFFSET/FETCH, not parameters
                    Visit(selectExpression.Offset);

                    Sql.Append(" ROWS");
                }

                if (selectExpression.Limit != null)
                {
                    if (selectExpression.Offset == null)
                    {
                        Sql.AppendLine();
                    }
                    else
                    {
                        Sql.Append(" ");
                    }

                    // Use FETCH FIRST when no offset, FETCH NEXT when there is an offset
                    if (selectExpression.Offset == null)
                    {
                        Sql.Append("FETCH FIRST ");
                    }
                    else
                    {
                        Sql.Append("FETCH NEXT ");
                    }

                    // OpenEdge requires literal values in OFFSET/FETCH, not parameters
                    Visit(selectExpression.Limit);

                    Sql.Append(" ROWS ONLY");
                }
            }
        }

        protected override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
        {
            // Handle COUNT(*) to cast result to INT to match EF Core expectations. This ensures that 'COUNT(*)' function is wrapped inside 'CAST (... AS INT)'.
            // The generated SQL will now be 'CAST(COUNT(*) AS INT)'
            // if (string.Equals(sqlFunctionExpression.Name, "COUNT", StringComparison.OrdinalIgnoreCase))
            // {
            //     Sql.Append("CAST(");
            //     base.VisitSqlFunction(sqlFunctionExpression);
            //     Sql.Append(" AS INT)");
            //     return sqlFunctionExpression;
            // }
            
            return base.VisitSqlFunction(sqlFunctionExpression);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            return base.VisitBinary(node);
        }

        protected override Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
        {
            if (sqlUnaryExpression.OperatorType == ExpressionType.Not && sqlUnaryExpression.Type == typeof(bool) && sqlUnaryExpression.Operand.GetType() == typeof(ColumnExpression)) 
            {
                ColumnExpression colExpr = sqlUnaryExpression.Operand as ColumnExpression;
                Sql.Append(string.Format("\"{0}\".\"{1}\" = 0", colExpr.TableAlias, colExpr.Name));
                return sqlUnaryExpression;
            }
            else
                return base.VisitSqlUnary(sqlUnaryExpression);
        }

        protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
        {
            Expression result;
            bool addEquals1ToLeft = false;
            bool addEquals1ToRight = false;

            if (sqlBinaryExpression.Left != null && sqlBinaryExpression.Left.Type == typeof(Boolean) && sqlBinaryExpression.Left.GetType() == typeof(ColumnExpression))
                addEquals1ToLeft = true;
            if (sqlBinaryExpression.Right != null && sqlBinaryExpression.Right.GetType() == typeof(SqlConstantExpression))
                addEquals1ToLeft = false;
            if (sqlBinaryExpression.Right != null && sqlBinaryExpression.Right.Type == typeof(Boolean) && sqlBinaryExpression.Right.GetType() == typeof(ColumnExpression))
                addEquals1ToRight = true;
            if (sqlBinaryExpression.Left != null && sqlBinaryExpression.Left.GetType() == typeof(SqlConstantExpression))
                addEquals1ToRight = false;

            if (addEquals1ToLeft || addEquals1ToRight)
            {
                var requiresParentheses = RequiresParentheses(sqlBinaryExpression, sqlBinaryExpression.Left);

                if (requiresParentheses)
                {
                    Sql.Append("(");
                }

                Visit(sqlBinaryExpression.Left);
                if (addEquals1ToLeft)
                    Sql.Append(" = 1 "); // copied base.VisitSqlBinary() just to add this line

                if (requiresParentheses)
                {
                    Sql.Append(")");
                }

                Sql.Append(GetOperator(sqlBinaryExpression));

                requiresParentheses = RequiresParentheses(sqlBinaryExpression, sqlBinaryExpression.Right);

                if (requiresParentheses)
                {
                    Sql.Append("(");
                }

                Visit(sqlBinaryExpression.Right);
                if (addEquals1ToRight)
                    Sql.Append(" = 1 "); // copied base.VisitSqlBinary() just to add this line

                if (requiresParentheses)
                {
                    Sql.Append(")");
                }

                return sqlBinaryExpression;
            }
            else
                result = base.VisitSqlBinary(sqlBinaryExpression);

            return result;
        }

        protected override Expression VisitConstant(ConstantExpression constantExpression)
        {
            // Handle DateTime values with OpenEdge-specific format
            if ((constantExpression.Type == typeof(DateTime) || constantExpression.Type == typeof(DateTime?))
                && constantExpression.Value != null)
            {
                var dateTime = (DateTime)constantExpression.Value;
                Sql.Append($"{{ ts '{dateTime:yyyy-MM-dd HH:mm:ss}' }}");
            }
            else
                base.VisitConstant(constantExpression);
            
            return constantExpression;
        }

        protected override Expression VisitTable(TableExpression tableExpression)
        {
            return base.VisitTable(tableExpression);
        }

        protected override void GenerateProjection(SelectExpression selectExpression)
        {
            TableExpression tableExpression = selectExpression.Tables.LastOrDefault() as TableExpression;
            if (tableExpression != null && tableExpression.Table != null)
            {
                // We need the last seen table name here in VisitRowNumber() to emulate ROW_NUMBER
                _lastSeenTableName = tableExpression.Table.Name;
                _lastSeenTableSchema = tableExpression.Table.Schema;
            }

            base.GenerateProjection(selectExpression);
        }

        protected override Expression VisitProjection(ProjectionExpression projectionExpression)
        {
            // OpenEdge doesn't support boolean expressions directly in SELECT clauses.
            // They must be wrapped in CASE statements: CASE WHEN condition THEN 1 ELSE 0 END
            if (projectionExpression.Expression.Type == typeof(bool))
            {
                if (projectionExpression.Expression.GetType() == typeof(SqlBinaryExpression) || projectionExpression.Expression.GetType() == typeof(SqlUnaryExpression))
                {
                    Sql.Append("CAST(CASE WHEN ");

                    Visit(projectionExpression.Expression);

                    Sql.Append(" THEN 1 ELSE 0 END AS BIT)");

                    // Handle alias if present
                    if (!string.IsNullOrEmpty(projectionExpression.Alias))
                    {
                        Sql.Append(" AS ");
                        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(projectionExpression.Alias));
                    }

                    return projectionExpression;
                }
            }

            // For non-boolean expressions, use the base implementation
            return base.VisitProjection(projectionExpression);
        }
    }
}
