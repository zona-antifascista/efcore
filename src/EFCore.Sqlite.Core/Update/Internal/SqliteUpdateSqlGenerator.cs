// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Sqlite.Internal;

namespace Microsoft.EntityFrameworkCore.Sqlite.Update.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class SqliteUpdateSqlGenerator : UpdateAndSelectSqlGenerator
{
    private readonly bool _isReturningClauseSupported;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public SqliteUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
        // Support for the RETURNING clause on INSERT/UPDATE/DELETE was added in Sqlite 3.35.
        // Detect which version we're using, and fall back to the older INSERT/UPDATE+SELECT behavior on legacy versions.
        _isReturningClauseSupported = new Version(new SqliteConnection().ServerVersion) >= new Version(3, 35);
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public override ResultSetMapping AppendInsertOperation(
            StringBuilder commandStringBuilder,
            IReadOnlyModificationCommand command,
            int commandPosition,
            out bool requiresTransaction)
        // We normally do a simple INSERT, with a RETURNING clause for generated columns or with "1" for concurrency checking.
        // However, older SQLite versions and virtual tables don't support RETURNING, so we do INSERT+SELECT.
        => CanUseReturningClause(command)
            ? AppendInsertReturningOperation(commandStringBuilder, command, commandPosition, out requiresTransaction)
            : AppendInsertAndSelectOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public override ResultSetMapping AppendUpdateOperation(
            StringBuilder commandStringBuilder,
            IReadOnlyModificationCommand command,
            int commandPosition,
            out bool requiresTransaction)
        // We normally do a simple UPDATE, with a RETURNING clause for generated columns or with "1" for concurrency checking.
        // However, older SQLite versions and virtual tables don't support RETURNING, so we do UPDATE+SELECT.
        => CanUseReturningClause(command)
            ? AppendUpdateReturningOperation(commandStringBuilder, command, commandPosition, out requiresTransaction)
            : AppendUpdateAndSelectOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public override ResultSetMapping AppendDeleteOperation(
            StringBuilder commandStringBuilder,
            IReadOnlyModificationCommand command,
            int commandPosition,
            out bool requiresTransaction)
        // We normally do a simple DELETE, with a RETURNING clause with "1" for concurrency checking.
        // However, older SQLite versions and virtual tables don't support RETURNING, so we do DELETE+SELECT.
        => CanUseReturningClause(command)
            ? AppendDeleteReturningOperation(commandStringBuilder, command, commandPosition, out requiresTransaction)
            : AppendDeleteAndSelectOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);

    /// <summary>
    ///     Appends a <c>WHERE</c> condition for the identity (i.e. key value) of the given column.
    /// </summary>
    /// <param name="commandStringBuilder">The builder to which the SQL should be appended.</param>
    /// <param name="columnModification">The column for which the condition is being generated.</param>
    protected override void AppendIdentityWhereCondition(StringBuilder commandStringBuilder, IColumnModification columnModification)
    {
        Check.NotNull(commandStringBuilder, nameof(commandStringBuilder));
        Check.NotNull(columnModification, nameof(columnModification));

        SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, "rowid");
        commandStringBuilder.Append(" = ")
            .Append("last_insert_rowid()");
    }

    /// <summary>
    ///     Appends a SQL command for selecting the number of rows affected.
    /// </summary>
    /// <param name="commandStringBuilder">The builder to which the SQL should be appended.</param>
    /// <param name="name">The name of the table.</param>
    /// <param name="schema">The table schema, or <see langword="null" /> to use the default schema.</param>
    /// <param name="commandPosition">The ordinal of the command for which rows affected it being returned.</param>
    /// <returns>The <see cref="ResultSetMapping" /> for this command.</returns>
    protected override ResultSetMapping AppendSelectAffectedCountCommand(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        int commandPosition)
    {
        Check.NotNull(commandStringBuilder, nameof(commandStringBuilder));
        Check.NotEmpty(name, nameof(name));

        commandStringBuilder
            .Append("SELECT changes()")
            .AppendLine(SqlGenerationHelper.StatementTerminator)
            .AppendLine();

        return ResultSetMapping.LastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly;
    }

    /// <summary>
    ///     Appends a <c>WHERE</c> condition checking rows affected.
    /// </summary>
    /// <param name="commandStringBuilder">The builder to which the SQL should be appended.</param>
    /// <param name="expectedRowsAffected">The expected number of rows affected.</param>
    protected override void AppendRowsAffectedWhereCondition(StringBuilder commandStringBuilder, int expectedRowsAffected)
    {
        Check.NotNull(commandStringBuilder, nameof(commandStringBuilder));

        commandStringBuilder.Append("changes() = ").Append(expectedRowsAffected);
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public override string GenerateNextSequenceValueOperation(string name, string? schema)
        => throw new NotSupportedException(SqliteStrings.SequencesNotSupported);

    private bool CanUseReturningClause(IReadOnlyModificationCommand command)
        => _isReturningClauseSupported && command.Table?.IsSqlReturningClauseUsed() == true;
}
